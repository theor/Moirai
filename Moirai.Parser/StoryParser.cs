using System.Diagnostics.CodeAnalysis;
using Moirai.Parser.Ast;
using Superpower.Model;

namespace Moirai.Parser;

public static class StoryParser
{
    public static bool GetFunctionDescriptor(string name, [NotNullWhen(true)] out FunctionDescriptor? descriptor)
    {
        descriptor = Functions.FirstOrDefault(f => f.FuncName == name);
        return descriptor != null;
    }

    public static readonly List<FunctionDescriptor> Functions =
    [
        new("create", true, ctx =>
        {
            // $var is declared in the enclosing scope (it persists after the create). An optional
            // `{ ... }` block is an initializer whose `prop := value` lines target the new entity.
            var variableIndex = ctx.ParseVariable(out var etid, out _);
            var name = ctx.ArgCount == 0 ? null : (InterpolatedString) ctx.ParseArgument(0);
            var scopeContext = ctx.GetScopeContext();
            IInstruction[]? init = null;
            if (scopeContext != null)
            {
                using var vs = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, scopeContext.Span);
                init = ctx.Visitor.ParseRawScope(scopeContext, out _);
            }

            return (new CreateEntity(variableIndex, etid, name, init), PropertyValue.TypeTypedRef(etid));
        }, new BuiltinDoc(DocCategory.Entities,
            ["create T $v", "create T $v: 'name'", "create T $v: 'name' { prop := value ... }"],
            "Creates a new T and binds it to $v, which stays in scope for the rest of the rule. The name is an interpolated string. The optional block sets properties on the new entity, one `prop := value` per line, before anything else sees it. Creating a singleton that already exists binds the existing one instead of making a second.",
            "create Person $p: '{roll(Name)}' {\n    age := 0\n    alive := true\n}")),
        new("each", true,
            ctx =>
            {
                var scopeContext = ctx.GetScopeContext();
                using var vs = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, scopeContext?.Span);
                var variableIndex = ctx.ParseVariable(out var etid, out _);
                return (new AssignPick(etid, variableIndex, ctx.ParsePredicateSql(etid),
                        CallType.Each, ctx.Visitor.ParseRawScope(scopeContext, out _)),
                    PropertyValue.TypeTypedRef(etid));
            }, new BuiltinDoc(DocCategory.Queries,
                ["each T $v: (predicate) { ... }"],
                "Runs the block once for every T the predicate matches, with $v bound to each in turn, oldest entity first. The matches are found before the block first runs, so entities it creates are not visited. A statement in the block that stops (a failed pick) ends only that iteration. `each` never fails, even when nothing matches.",
                "each Person $p: (alive, age > 60) {\n    set $p.wealth = $p.wealth + 1\n}")),
        new("pick", true,
            ctx =>
            {
                // The fallback is parsed before $v is declared: it runs exactly when there is no $v.
                IInstruction[]? orElse = null;
                if (ctx.GetElseContext() is { } elseContext)
                {
                    using var vs = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, elseContext.Span);
                    orElse = ctx.Visitor.ParseRawScope(elseContext, out _);
                }

                var variableIndex = ctx.ParseVariable(out var etid, out _);
                return (new AssignPick(etid, variableIndex, ctx.ParsePredicateSql(etid),
                        CallType.Pick, elseEffects: orElse),
                    PropertyValue.TypeTypedRef(etid));
            }, new BuiltinDoc(DocCategory.Queries,
                ["pick T $v: (predicate)", "pick T $v: (predicate) else { ... }"],
                "Picks one T the predicate matches, uniformly at random, and binds it to $v. When nothing matches, the rule stops there and has failed: what it already did stays in the world, but its changes are not logged and no trigger sees them, so pick before changing anything. With `else`, the block runs instead and the rule then stops *successfully*: its changes are logged and triggers see them. The block cannot see $v. As a condition, `if (pick T $v: (...)) { }` tests whether anything matched.",
                "pick Person $p: (alive, partner = null) else {\n    record('Nobody is left to marry')\n}\nrecord('{$p.name} stays single')")),

        new("schedule", false, ctx =>
        {
            // schedule(entity, year) { body } — defer `body` (with `entity` bound as $self) to fire once
            // the simulation reaches `year`. Both args are evaluated now, in the enclosing scope; only the
            // body is deferred. The body sees $self (the bound entity) but NOT the enclosing locals.
            ctx.ExpectArgcount(2);
            var entity = ctx.ParseArgument(0, out var entityType);
            var year = ctx.ParseArgument(1);

            var scopeContext = ctx.GetScopeContext();
            if (scopeContext == null)
            {
                ctx.Visitor.AddError(ErrorCode.MissingEachScope, ctx.CallContext.Span,
                    "schedule requires a { } body");
                return (null!, PropertyValue.ValueType.Null);
            }

            // Taken before the body is parsed, so a schedule nested in this one is numbered after it.
            var scheduleKey = ctx.Visitor.NextScheduleStreamKey();
            int selfVarIndex;
            IInstruction[] body;
            Moirai.Core.DebugScope? debugScope;
            using (var vs = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, scopeContext.Span))
            {
                // $self takes the static type of the target entity expression, so `$self.prop` resolves.
                ctx.Visitor.DeclareVar("$self", entityType, ctx.GetArgumentToken(0)!.Span, out selfVarIndex);
                body = ctx.Visitor.ParseRawScope(scopeContext, out _);
                // Capture the body's scope (with $self) so the debugger can show locals when stopped here.
                debugScope = ctx.Visitor.CaptureCurrentDebugScope();
            }

            // The body runs later via Database.RunAction, so wrap it as a standalone EventTrigger (not added
            // to Actions/Triggers, so it never auto-fires). The high id base keeps schedule sites from
            // colliding with real event ids in the profiler's per-id stats table.
            var site = new EventTrigger(1_000_000 + ctx.Visitor.Database.ScheduleSiteCount,
                $"schedule@{ctx.CallContext.Span.Position.Line}", false, null)
            {
                DebugScopeRoot = debugScope,
                Line = ctx.CallContext.Span.Position.Line,
                IsScheduled = true,
                RngKey = scheduleKey,
            };
            site.Effects.AddRange(body);
            var siteIndex = ctx.Visitor.Database.RegisterScheduleSite(site, selfVarIndex);

            return (new ScheduleEffect(entity, year, siteIndex, body), PropertyValue.ValueType.Null);
        }, new BuiltinDoc(DocCategory.Rules,
            ["schedule(entity, year) { ... }"],
            "Defers the block until the simulation reaches `year`, then runs it once, as a rule of its own, with $self bound to `entity`. Both arguments are evaluated now. The block sees $self but none of the enclosing rule's locals, so read what it needs from $self. A year that is not in the future fires next year. If the entity no longer exists by then, nothing runs, so a block that cares whether $self is still alive should test it.",
            "pick Person $p: (alive)\nschedule($p, #Time.year + 20) {\n    if $self.alive {\n        set $self.age = $self.age + 20\n    }\n}")),
        new("assert", false, ctx =>
            (new AssertInstr(ctx.ParseArgument(0), ctx.GetText(ctx.GetArgumentToken(0)!.Span)),
                PropertyValue.ValueType.Null),
            new BuiltinDoc(DocCategory.Testing,
                ["assert(condition)"],
                "Stops the simulation with an error when the condition is false. The error quotes the condition as written.",
                "pick Person $p: (alive)\nassert($p.age >= 0)")),
        new("assert_eq", false, ctx =>
            (new AssertInstr(
                    ctx.ParseArgument(0),
                    ctx.ParseArgument(1),
                    $"{ctx.GetText(ctx.GetArgumentToken(0)!.Span)} = {ctx.GetText(ctx.GetArgumentToken(1)!.Span)}"),
                PropertyValue.ValueType.Null),
            new BuiltinDoc(DocCategory.Testing,
                ["assert_eq(actual, expected)"],
                "Stops the simulation with an error when the two values differ. The error quotes both expressions as written and prints both values.",
                "create Person $p: 'Ada' {\n    age := 3\n}\nassert_eq($p.age, 3)")),
        new("mark", false, ctx =>
        {
            ctx.ExpectArgcount(1);
            var e = ctx.ParseArgument(0);
            return (new Mark(e, ctx.Visitor.CurrentEventTrigger!.Id), PropertyValue.ValueType.Null);
        }, new BuiltinDoc(DocCategory.Rules,
            ["mark(entity)"],
            "Remembers that this rule touched `entity` this year, for `since_last` to read. Marks belong to the rule that makes them, so pair `mark` and `since_last` in the same rule. Use the pair to keep a rule from picking the same entity again too soon.",
            "pick Person $p: (alive, since_last($p) > 4)\nmark($p)\nrecord('{$p.name} goes on a pilgrimage')")),
        new("since_last", false, ctx =>
        {
            ctx.ExpectArgcount(1);
            var e = ctx.ParseArgument(0);
            return (new SinceLast(e, ctx.Visitor.CurrentEventTrigger!.Id), PropertyValue.TypeNumber);
        }, new BuiltinDoc(DocCategory.Rules,
            ["since_last(entity)"],
            "The number of years since this rule last called `mark(entity)`. An entity this rule never marked counts as marked in year 0, so it reads as long ago rather than as a special value. Usable in a pick's predicate.",
            "pick Person $p: (alive, since_last($p) > 10)\nmark($p)")),
        new("related", false, ctx =>
        {
            ctx.ExpectArgcount(3);
            var a = ctx.ParseArgument(0, out var aType);
            var b = ctx.ParseArgument(1);
            var degreeArg = ctx.ParseArgument(2);
            var span = ctx.CallContext.Span;

            if (degreeArg is not Literal { Value.Type.BaseType: PropertyValue.ValueBaseType.Number } degreeLit
                || degreeLit.Value.IntValue < 0 || degreeLit.Value.IntValue > Related.MaxDegree)
            {
                ctx.Visitor.AddError(ErrorCode.InvalidArgument, ctx.GetArgumentToken(2)?.Span ?? span,
                    $"related() takes a degree from 0 to {Related.MaxDegree} as a number literal");
                return (null!, PropertyValue.TypeBool);
            }

            // Parents are resolved here, once, from the first argument's type's parent roles
            // (@parents, or parent1/parent2 by default) -- the same ones the viewer's family tree reads.
            var type = aType.IsRefType && aType.Index != 0
                ? ctx.Visitor.Database.GetEntityType(new EntityTypeId(aType.Index))
                : null;
            var p1 = type?.Role(EntityRole.Parent1) ?? default;
            var p2 = type?.Role(EntityRole.Parent2) ?? default;
            if (!p1.IsValid || !p2.IsValid)
            {
                ctx.Visitor.AddError(ErrorCode.UnknownProperty, ctx.GetArgumentToken(0)?.Span ?? span,
                    "related() needs an entity whose type has parents: declare parent1 and parent2, or name them with @parents(a, b)");
                return (null!, PropertyValue.TypeBool);
            }

            return (new Related(a, b, degreeLit.Value.IntValue, p1, p2), PropertyValue.TypeBool);
        },
        new BuiltinDoc(DocCategory.Kinship,
            ["related($a, $b, n)"],
            "True when $a and $b share an ancestor within n degrees of kinship, counted the civil-law way: parent 1, grandparent or sibling 2, aunt or uncle 3, first cousin 4. `n` is a number literal from 0 to 6. The parents are the type's `@parents`, or `parent1`/`parent2` by default, and the type must have them.",
            "pick Person $x: (alive, partner = null)\npick Person $y: (alive, partner = null, $y != $x, not(related($x, $y, 4)))")),
        new("record", false, ctx =>
        {
            ctx.ExpectArgcount(2, isMaxCount: true);
            var interpolatedString = (InterpolatedString) ctx.ParseArgument(0);
            IValue? weight = null;
            // Only the () form can carry a weight: the bare `record '...'` form has a single argument, and
            // asking it for a second reports "convert to () syntax".
            if (ctx.ArgCount > 1)
            {
                weight = ctx.ParseArgument(1, out var weightType);
                if (weightType.BaseType is not (PropertyValue.ValueBaseType.Number or PropertyValue.ValueBaseType.Float))
                    ctx.Visitor.AddError(ErrorCode.InvalidArgument, ctx.GetArgumentToken(1)?.Span ?? ctx.CallContext.Span,
                        "record() weight must be a number");
            }

            return (new Record(interpolatedString, weight), PropertyValue.ValueType.Null);
        },
        new BuiltinDoc(DocCategory.Records,
            ["record('text')", "record('text', weight)"],
            "Writes a sentence into the world's history. The text is interpolated: `{$p.name}` inserts a value, and an entity mentioned this way becomes a participant of the record, so it links to that entity and shows on its Life page. The optional weight, any number expression (taken as a whole number), says how much the record matters: 1 by default, 0 for background noise, higher for the turning points the chronicle surfaces. The weight is metadata only and never changes how the world runs. The older bare form `record 'text'` takes no weight.",
            "pick Person $p: (alive)\nrecord('{$p.name} is crowned', 5)")),
        new("link", false, ctx =>
        {
            var linkValue = ctx.ParseArgument(0);
            var linkText = ctx.ParseArgument(1);
            return (new InterpolatedStringLink(linkValue, linkText), PropertyValue.TypeString);
        }, new BuiltinDoc(DocCategory.Records,
            ["link(entity, 'text')"],
            "Inside a record's text, shows `text` as a link to `entity`. Use it when the words should not be the entity's name. Unlike `{$p.name}`, a link does not make the entity one of the record's participants.",
            "pick Person $p: (alive)\nrecord('{$p.name} paints a {link($p, 'self-portrait')}')")),
        new("call", false, ctx =>
        {
            var arg = ctx.GetArgumentToken(0);
            string? eventName = arg?.Value?.Path != null
                ? arg.Value.Path.Span.ToStringValue()
                : arg?.Value?.StringLit?.GetString();
            if (eventName == null)
            {
                ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext.Span, "event name");
                return (null!, PropertyValue.ValueType.Null);
            }

            int count = 1;
            if (ctx.ArgCount > 1)
            {
                var countValue = ctx.ParseArgument(1);
                if (countValue is Literal {
                        Value.Type.BaseType: PropertyValue.ValueBaseType.Number
                    } l)
                {
                    count = l.Value.IntValue;
                }
            }

            // call() invokes either a scheduled event (run via RunAction, own changeset + triggers)
            // or a procedural function (run inline in the caller's changeset).
            var eventIndex = ctx.Visitor.Database.Actions.FindIndex(r => r.Name == eventName);
            if (eventIndex != -1)
            {
                // A parameterized event takes the trailing call() args as its arguments (the count
                // form is only for zero-parameter events).
                var pars = ctx.Visitor.Database.Actions[eventIndex].Parameters;
                if (pars is { Count: > 0 })
                {
                    var args = new IValue[pars.Count];
                    for (int i = 0; i < pars.Count; i++)
                    {
                        if (i + 1 >= ctx.ArgCount)
                        {
                            ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext.Span,
                                $"call({eventName}) is missing argument {pars[i].ParamName}: {ctx.Visitor.Database.Printer.Print(pars[i].ParamType)}");
                            args[i] = new Literal(0);
                            continue;
                        }

                        var av = ctx.ParseArgument(i + 1, out var at);
                        if (!AstVisitor.Accepts(pars[i].ParamType, at))
                            ctx.Visitor.AddError(ErrorCode.MismatchedAssignmentTypes,
                                ctx.GetArgumentToken(i + 1)?.Span ?? ctx.CallContext.Span,
                                $"Expected {ctx.Visitor.Database.Printer.Print(pars[i].ParamType)} got {ctx.Visitor.Database.Printer.Print(at)}");
                        args[i] = av;
                    }

                    return (new CallRule(eventIndex, args), PropertyValue.ValueType.Null);
                }

                return (new CallRule(eventIndex, count), PropertyValue.ValueType.Null);
            }

            var funcIndex = ctx.Visitor.Database.Functions
                .FindIndex(f => f.Name == eventName && !f.IsInstanceMethod);
            if (funcIndex != -1)
                return (new CallFunction(ctx.Visitor.Database.Functions[funcIndex], count),
                    PropertyValue.ValueType.Null);

            ctx.Visitor.AddError(ErrorCode.UnknownRule, arg?.Span ?? ctx.CallContext.Span, eventName);
            return (null!, PropertyValue.ValueType.Null);
        }, new BuiltinDoc(DocCategory.Rules,
            ["call(event)", "call(event, n)", "call(event, arg1, arg2, ...)", "call(function)"],
            "Runs an event now, from inside another rule. The event runs as a rule of its own: its changes are logged and trigger reactions like a scheduled event's, and the caller's own changes carry on around it. `n`, a number literal, runs it that many times. An event declared with parameters, `event greet($who: Person) { }`, takes its arguments instead, checked against their types. `call` also runs a `function` that returns nothing (a procedure), inline in the caller's rule. The event must be written above the rule that calls it, and only an event or a trigger can call one: a function cannot.",
            "call(harvest, 3)\npick Person $p: (alive)\ncall(greet, $p)\ncall(feast)")),

        new("random", false, ctx =>
        {
            var argCount = ctx.ArgCount;
            if (argCount == 0)
            {
                ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext.Span,
                    "'random' needs at least one argument");
                return (null!, PropertyValue.ValueType.Null);
            }

            var arg = ctx.ParseArgument(0);

            if (arg is Literal {Value.Type.BaseType: PropertyValue.ValueBaseType.EnumType} l)
            {
                ctx.ExpectArgcount(1);
                var edid = new EnumDefinitionId((ushort) l.Value.IntValue);
                return (new RandomEnum(edid), PropertyValue.TypeEnum(edid));
            }

            if (arg is Literal {Value.Type.BaseType: PropertyValue.ValueBaseType.Number})
            {
                ctx.ExpectArgcount(2, true);
                var min = argCount == 1 ? new Literal(0) : arg;
                var max = ctx.ParseArgument(argCount == 1 ? 0 : 1);
                return (new RandomRange(min, max), PropertyValue.TypeNumber);
            }

            ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext.Span, ctx.GetText(ctx.CallContext.Span));
            return (null!, PropertyValue.ValueType.Null);
        }, new BuiltinDoc(DocCategory.Randomness,
            ["random(EnumName)", "random(max)", "random(min, max)"],
            "`random(EnumName)` is one of the enum's values, each equally likely. `random(max)` is a whole number from 0 to max - 1, and `random(min, max)` one from min to max - 1: the upper bound is never drawn. When max is not above min, the result is min. The first argument must be a number literal (or the enum); the second can be any number expression. Every call draws from the rule's own random stream, so the world stays the same for a given seed.",
            "pick Person $x: (alive)\nset $x.job = random(Job)\nif random(100) < 8 {\n    set $x.wealth = random(40, 90)\n}")),
        new("chance", false, ctx =>
        {
            ctx.ExpectArgcount(1);
            var span = ctx.GetArgumentToken(0)?.Span ?? ctx.CallContext.Span;
            // A query's narrowing decides which candidates its predicate is evaluated on, so a draw
            // inside one would make the world depend on the index -- pick first, then roll.
            if (ctx.Visitor.InSqlPredicate)
                ctx.Visitor.AddError(ErrorCode.InvalidArgument, ctx.CallContext.Span,
                    "chance() cannot be part of a pick/each predicate; pick first, then test chance()");
            var p = ctx.ParseArgument(0, out var pType);
            if (pType.BaseType is not (PropertyValue.ValueBaseType.Percentage or PropertyValue.ValueBaseType.Number
                or PropertyValue.ValueBaseType.Float))
                ctx.Visitor.AddError(ErrorCode.InvalidArgument, span, "chance() takes a percentage, e.g. chance(3%)");

            var scopeContext = ctx.GetScopeContext();
            IInstruction[]? body = null;
            if (scopeContext != null)
            {
                using var vs = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, scopeContext.Span);
                body = ctx.Visitor.ParseRawScope(scopeContext, out _);
            }

            return (new Chance(p, body), body == null ? PropertyValue.TypeBool : PropertyValue.ValueType.Null);
        }, new BuiltinDoc(DocCategory.Randomness,
            ["chance(p)", "chance(p) { ... }"],
            "True with probability p, a percentage: `chance(3%)`. It always draws exactly once, whatever p is. As a statement with a block, the block runs when the draw hits, and the rule carries on either way. It cannot be part of a pick or each predicate: pick first, then test chance.",
            "pick Person $p: (alive)\nchance(5%) {\n    record('{$p.name} finds a fortune')\n}\nif chance(50%) {\n    set $p.wealth = $p.wealth + 1\n}")),
        new("roll", false, ctx =>
        {
            // roll(TableName) — sample a named weighted table. The arg is a bare table name (a
            // TYPE_ID), looked up by text rather than parsed as a value (it isn't an enum/entity).
            if (ctx.ArgCount != 1)
            {
                ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext.Span, "'roll' takes one table name");
                return (null!, PropertyValue.ValueType.Null);
            }

            var tableSpan = ctx.GetArgumentToken(0)!.Span;
            var tableName = ctx.GetText(tableSpan);
            if (!ctx.Visitor.Database.GetTableDefinition(tableName, out var table))
            {
                ctx.Visitor.AddError(ErrorCode.UnknownTable, ctx.CallContext.Span, tableName);
                return (null!, PropertyValue.ValueType.Null);
            }

            ctx.Visitor.Linker?.LinkTable(new FileRange(tableSpan), table);

            return (new RollTable(table.Id, table.Name), table.ValueType);
        }, new BuiltinDoc(DocCategory.Randomness,
            ["roll(TableName)"],
            "Draws one entry from a `table`, according to its weights. The result has the type of the table's entries.",
            "create Person $p: '{roll(Name)}'")),
        new("add", false, ctx =>
        {
            ctx.ExpectArgcount(2);
            if (ctx.ParseCollectionPath(0, out var full, out var owner, out var coll))
                return (new CollectionMutate(full, owner, coll, ctx.ParseArgument(1), true),
                    PropertyValue.ValueType.Null);
            return (null!, PropertyValue.ValueType.Null);
        }, new BuiltinDoc(DocCategory.Collections,
            ["add($e.coll, value)"],
            "Adds a value to a collection property (`prop friends: [Person]`). A collection is a set: adding a value it already holds changes nothing.",
            "pick Person $a: (alive)\npick Person $b: (alive, $b != $a)\nadd($a.friends, $b)")),
        new("remove", false, ctx =>
        {
            ctx.ExpectArgcount(2);
            if (ctx.ParseCollectionPath(0, out var full, out var owner, out var coll))
                return (new CollectionMutate(full, owner, coll, ctx.ParseArgument(1), false),
                    PropertyValue.ValueType.Null);
            return (null!, PropertyValue.ValueType.Null);
        }, new BuiltinDoc(DocCategory.Collections,
            ["remove($e.coll, value)"],
            "Removes a value from a collection property. Removing a value it does not hold changes nothing.",
            "pick Person $a: (alive)\npick Person $b: (contains($a.friends, $b))\nremove($a.friends, $b)")),
        new("contains", false, ctx =>
        {
            ctx.ExpectArgcount(2);
            if (ctx.ParseCollectionPath(0, out var full, out var owner, out var coll))
                return (new CollectionQuery(CollectionQuery.QueryKind.Contains, full, owner, coll,
                    ctx.ParseArgument(1)), PropertyValue.TypeBool);
            return (null!, PropertyValue.TypeBool);
        }, new BuiltinDoc(DocCategory.Collections,
            ["contains($e.coll, value)"],
            "True when the collection property holds the value. Usable in a pick's predicate.",
            "pick Person $a: (alive)\npick Person $b: (alive, $b != $a, not(contains($a.friends, $b)))")),
        new("sum", true, ctx => ParseAggregate(ctx, Aggregate.AggregateKind.Sum),
            new BuiltinDoc(DocCategory.Queries,
                ["sum T $v: (predicate..., value)"],
                "The total of `value` over every T the predicate matches, 0 when none does. The arguments before the last are the predicate, joined by `and` like a pick's. $v exists only inside the call. A sum of percentages is a plain number, since it can pass 100. Draws no random numbers of its own, and can sit inside another query's predicate.",
                "var $total: sum Person $p: (alive, $p.wealth)")),
        new("avg", true, ctx => ParseAggregate(ctx, Aggregate.AggregateKind.Avg),
            new BuiltinDoc(DocCategory.Queries,
                ["avg T $v: (predicate..., value)"],
                "The mean of `value` over every T the predicate matches, 0 when none does: use `count` to tell none from zero. Written like `sum`.",
                "var $mean: avg Person $p: (alive, $p.happiness)")),
        new("min", true, ctx => ParseAggregate(ctx, Aggregate.AggregateKind.Min),
            new BuiltinDoc(DocCategory.Queries,
                ["min T $v: (predicate..., value)"],
                "The smallest `value` over every T the predicate matches, 0 when none does. Written like `sum`.",
                "var $youngest: min Person $p: (alive, $p.age)")),
        new("max", true, ctx => ParseAggregate(ctx, Aggregate.AggregateKind.Max),
            new BuiltinDoc(DocCategory.Queries,
                ["max T $v: (predicate..., value)"],
                "The largest `value` over every T the predicate matches, 0 when none does. Written like `sum`.",
                "var $oldest: max Person $p: (alive, $p.age)")),
        new("count", false, ctx =>
        {
            // `count T $v: (predicate)` counts a query; `count($e.coll)` a collection.
            if ((ctx.CallContext.Call?.DeclType ?? ctx.CallContext.RawCall?.DeclType) != null)
                return ParseAggregate(ctx, Aggregate.AggregateKind.Count);
            ctx.ExpectArgcount(1);
            if (ctx.ParseCollectionPath(0, out var full, out var owner, out var coll))
                return (new CollectionQuery(CollectionQuery.QueryKind.Count, full, owner, coll, null),
                    PropertyValue.TypeNumber);
            return (null!, PropertyValue.TypeNumber);
        }, new BuiltinDoc(DocCategory.Queries,
            ["count T $v", "count T $v: (predicate...)", "count($e.coll)"],
            "`count T $v: (predicate)` is how many T the predicate matches; with no predicate, how many T exist at all. $v exists only inside the call. `count($e.coll)` is the number of values in a collection property. Draws no random numbers, and can sit inside another query's predicate.",
            "var $living: count Person $p: (alive)\npick Person $p: (alive, count($p.friends) < 3)")),
        new("not", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Not, ctx.ParseArgument(0)), PropertyValue.TypeBool),
            new BuiltinDoc(DocCategory.Math,
                ["not(condition)"],
                "True when the condition is false. Usable in a pick's predicate.",
                "pick Person $p: (not($p.alive))")),
        new("floor", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Floor, ctx.ParseArgument(0)), PropertyValue.TypeNumber),
            new BuiltinDoc(DocCategory.Math,
                ["floor(x)"],
                "x rounded down to a whole number.",
                "var $half: floor(count Person $p: (alive) / 2)")),
        new("round", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Round, ctx.ParseArgument(0)), PropertyValue.TypeNumber),
            new BuiltinDoc(DocCategory.Math,
                ["round(x)"],
                "x rounded to the nearest whole number. A half rounds to the even neighbour: round(2.5) is 2, round(3.5) is 4.",
                "var $mean: round(avg Person $p: (alive, $p.age))")),
        new("ceiling", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Ceiling, ctx.ParseArgument(0)), PropertyValue.TypeNumber),
            new BuiltinDoc(DocCategory.Math,
                ["ceiling(x)"],
                "x rounded up to a whole number.",
                "var $boats: ceiling(count Person $p: (alive) / 12)")),
        new("clamp01", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Clamp01, ctx.ParseArgument(0)), PropertyValue.TypeNumber),
            new BuiltinDoc(DocCategory.Math,
                ["clamp01(x)"],
                "x limited to the range 0 to 1. It is for fractions: a percentage is held as 0 to 100 and is already kept in that range whenever it is set.",
                "var $share: clamp01(count Person $p: (alive) / 1000)")),
        new("debug", false,
            ctx => (
                new DebugPrint(Enumerable.Repeat((object?) null, ctx.ArgCount).Select((_, i) => ctx.ParseArgument(i))),
                PropertyValue.ValueType.Null),
            new BuiltinDoc(DocCategory.Testing,
                ["debug(value, ...)"],
                "Prints each argument, as written and as it evaluates, to the host's console (the server's, or the browser's developer console). It changes nothing in the world.",
                "pick Person $p: (alive)\ndebug($p.name, $p.age)"))
    ];

    /// Every attribute the language accepts. This is the registry, not just the documentation: the parser
    /// reports an attribute that is not listed here for its kind of definition before lowering it, so an
    /// attribute cannot be added without being documented.
    public static readonly List<AttributeDescriptor> Attributes =
    [
        new("start", AttributeTarget.Event, [], DocCategory.EventAttributes,
            "Runs the event once, when the world is created, before the first year passes. This is where a story sets the clock (`set #Time.year = 764`) and makes its first entities. Several `@start` events run in the order they are written.",
            "@start\nevent founding {\n    create Person $p: 'Ada' {\n        alive := true\n    }\n}"),
        new("frequency", AttributeTarget.Event,
            [Arg.Number("x", min: 1), Arg.Choice<Database.Frequency>("mode"), Arg.Number("y", min: 1)],
            DocCategory.EventAttributes,
            "Schedules the event. `PerXYear` is random: on average x runs every y years, drawn each year, so a year can have none or several. `EveryXYear` is exact: x runs in every window of y years, each on a year of the window drawn at random. An event with no scheduling attribute runs only when something calls it.",
            "@frequency(1, PerXYear, 2)\nevent quarrel {\n    record('A quarrel')\n}\n@frequency(1, EveryXYear, 10)\nevent census {\n    record('A census')\n}"),
        new("tag", AttributeTarget.Event | AttributeTarget.Trigger, [Arg.String("name").Repeated()],
            DocCategory.EventAttributes,
            "Labels an event or trigger with one or more names. The viewer's Records page groups and filters records by them.",
            "@tag('family', 'noise')\n@frequency(1, PerXYear, 1)\nevent chatter {\n    record('People talk', 0)\n}"),
        new("display", AttributeTarget.Type,
            [Arg.EntityType("OtherType"), Arg.String("Label"), Arg.Predicate("predicate", over: 0), Arg.Text("item format").Optional()],
            DocCategory.TypeAttributes,
            "Adds a derived field to the entity's details in the viewer: every OtherType the predicate matches, listed under Label, each written with the item format when there is one.",
            "@display(Kin, 'Children', parent1 = $self or parent2 = $self)\n@display(Kin, 'Friends', contains($self.friends, $other), '{$other.name}, aged {$other.age}')\nentity Kin {\n    prop age: number\n    prop parent1: Kin\n    prop parent2: Kin\n    prop friends: [Kin]\n}"),
        new("parents", AttributeTarget.Type,
            [Arg.Property("a", PropertyKind.SelfReference), Arg.Property("b", PropertyKind.SelfReference)],
            DocCategory.TypeAttributes,
            "Names the two properties that hold an entity's parents. `related()`, the family tree and the Life page read them. Without it, properties named `parent1` and `parent2` are used.",
            "@parents(mother, father)\nentity Kin {\n    prop mother: Kin\n    prop father: Kin\n}"),
        new("partner", AttributeTarget.Type, [Arg.Property("p", PropertyKind.SelfReference)],
            DocCategory.TypeAttributes,
            "Names the property that holds an entity's partner, shown in the family tree. Defaults to a property named `partner`.",
            "@partner(spouse)\nentity Kin {\n    prop spouse: Kin\n}"),
        new("born", AttributeTarget.Type, [Arg.Property("p", PropertyKind.Number)],
            DocCategory.TypeAttributes,
            "Names the property that holds the year an entity was born. Defaults to `birthdate`, and then to the year the entity was created.",
            "@born(born_in)\nentity Kin {\n    prop born_in: number\n}"),
        new("died", AttributeTarget.Type, [Arg.Property("p", PropertyKind.Number)],
            DocCategory.TypeAttributes,
            "Names the property that holds the year an entity died. Defaults to `deathdate`.",
            "@died(died_in)\nentity Kin {\n    prop died_in: number\n}"),
        new("alive", AttributeTarget.Type, [Arg.Property("p", PropertyKind.Bool)],
            DocCategory.TypeAttributes,
            "Names the property that is true while an entity lives. The viewer counts the living with it. Defaults to `alive`. A type takes `@alive` or `@dead`, not both.",
            "@alive(breathing)\nentity Kin {\n    prop breathing: bool\n}"),
        new("dead", AttributeTarget.Type, [Arg.Property("p", PropertyKind.Bool)],
            DocCategory.TypeAttributes,
            "Names a property that is true once an entity has died, for a story that tracks death rather than life. A type takes `@alive` or `@dead`, not both.",
            "@dead(fallen)\nentity Kin {\n    prop fallen: bool\n}"),
        new("period", AttributeTarget.Type,
            [Arg.Property("from", PropertyKind.Number), Arg.Property("to", PropertyKind.Number)],
            DocCategory.TypeAttributes,
            "Marks the type as an age of history, from its first year to its last. The chronicle on the Home page and the exported history are chaptered by these. Without it, a type with `start_year` and `end_year` and no reference properties counts as one.",
            "@period(from, to)\nentity Era {\n    prop from: number\n    prop to: number\n}"),
        new("population", AttributeTarget.Type, [], DocCategory.TypeAttributes,
            "Marks the type whose living count is the world's population, plotted on the Home page. At most one type can carry it. Without it, the largest type with an alive or dead role is used.",
            "@population\nentity Villager {\n    prop alive: bool\n}"),
    ];

    public static AttributeDescriptor? GetAttribute(string name, AttributeTarget target) =>
        Attributes.Find(a => a.Name == name && a.Targets.HasFlag(target));

    /// `count T $v: (pred...)` and `sum|avg|min|max T $v: (pred..., value)`. Like a pick, the arguments
    /// before the value are the predicate, joined by `and`; $v exists only inside the call.
    static (IValueCall, PropertyValue.ValueType) ParseAggregate(FunctionParseContext ctx, Aggregate.AggregateKind kind)
    {
        var call = ctx.CallContext.Call;
        // `count T $v`, with no predicate at all, is the bare form: every T.
        if (call == null && kind == Aggregate.AggregateKind.Count && ctx.CallContext.RawCall is { DeclType: not null, Value: null } raw)
        {
            using var rawScope = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, raw.Span);
            var index = ctx.ParseVariable(out var type, out _);
            return (new Aggregate(kind, type, index, null, null, PropertyValue.TypeNumber), PropertyValue.TypeNumber);
        }

        if (call?.DeclType == null)
        {
            ctx.Visitor.AddError(ErrorCode.MissingVariable, ctx.CallContext.Span,
                $"{kind.ToString().ToLowerInvariant()} needs a query: {kind.ToString().ToLowerInvariant()} T $v: (predicate{(kind == Aggregate.AggregateKind.Count ? "" : ", value")})");
            return (null!, PropertyValue.TypeNumber);
        }

        using var vs = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, call.Span);
        var variableIndex = ctx.ParseVariable(out var etid, out _);

        int predicateCount = kind == Aggregate.AggregateKind.Count ? ctx.ArgCount : ctx.ArgCount - 1;
        if (predicateCount < 0)
        {
            ctx.Visitor.AddError(ErrorCode.MissingArgument, call.Span,
                $"{kind.ToString().ToLowerInvariant()} needs the value to add up, after the predicate: (predicate, $v.prop)");
            return (null!, PropertyValue.TypeNumber);
        }

        IValueSql? predicate = null;
        if (predicateCount > 0)
        {
            ctx.Visitor.InSqlPredicateDepth++;
            try
            {
                var parts = new IValue[predicateCount];
                for (int i = 0; i < predicateCount; i++)
                    parts[i] = ctx.ParseArgument(i);
                var p = predicateCount == 1 ? parts[0] : new And(parts);
                if (p is IValueSql sql)
                    predicate = sql;
                else
                    ctx.Visitor.AddError(ErrorCode.ExpectedSql, ctx.GetArgumentToken(0)?.Span ?? call.Span,
                        "Expected a predicate");
            }
            finally
            {
                ctx.Visitor.InSqlPredicateDepth--;
            }
        }

        if (kind == Aggregate.AggregateKind.Count)
            return (new Aggregate(kind, etid, variableIndex, predicate, null, PropertyValue.TypeNumber),
                PropertyValue.TypeNumber);

        var value = ctx.ParseArgument(predicateCount, out var valueType);
        if (valueType.BaseType is not (PropertyValue.ValueBaseType.Number or PropertyValue.ValueBaseType.Float
            or PropertyValue.ValueBaseType.Percentage))
        {
            ctx.Visitor.AddError(ErrorCode.InvalidArgument, ctx.GetArgumentToken(predicateCount)?.Span ?? call.Span,
                $"{kind.ToString().ToLowerInvariant()} adds up numbers; this value is {ctx.Visitor.Database.Printer.Print(valueType)}");
            return (null!, PropertyValue.TypeNumber);
        }

        var resultType = (kind, valueType.BaseType) switch
        {
            (Aggregate.AggregateKind.Sum, PropertyValue.ValueBaseType.Percentage) => PropertyValue.TypeFloat,
            (Aggregate.AggregateKind.Avg, PropertyValue.ValueBaseType.Number) => PropertyValue.TypeFloat,
            _ => valueType,
        };
        return (new Aggregate(kind, etid, variableIndex, predicate, value, resultType), resultType);
    }

    public interface IVisitor
    {
        List<Error> Errors { get; }
        (int offsetLine, int offsetColumn) Offset { get; set; }
    }

    public enum ErrorCode
    {
        Lexer,
        Parser,
        UnknownCall,
        UnknownExpressionOperator,
        UnknownProperty,
        DuplicatePropertyDefinition,
        UnknownPropertyType,
        UnknownEnumValue,
        DuplicateVariableDefinition,
        MissingEachScope,
        UnknownEnum,
        TypenameMustStartWithUpperCase,
        VariableNotDeclared,
        NullEffect,
        UnknownInstruction,
        MissingArgument,
        UnknownRule,
        UnknownEntityType,
        Exception,
        UnknownTag,
        DuplicateTagDefinition,
        WeightMatchTakesOnlyOneValue,
        MatchNullWeight,
        MatchAnyValueMustBeLast,
        MissingVariable,
        UnknownAttribute,
        UnknownFunction,
        MismatchedAssignmentTypes,
        MissingReturnValue,
        MismatchedReturnType,
        ExpectedSql,
        ExpectedCollection,
        RedundantTypeFilter,
        FunctionInlinedToSql,
        DuplicateDefinition,
        UnknownTable,
        InvalidArgument,
    }

    /// <summary>How a <see cref="Error"/> should be surfaced. Defaults to <see cref="Error"/> (value 0)
    /// so existing diagnostics are unaffected; <see cref="Warning"/> is used for non-fatal lints such as
    /// <see cref="ErrorCode.RedundantTypeFilter"/>.</summary>
    public enum Severity
    {
        Error,
        Warning,
        Information,
    }

    public struct Error
    {
        public readonly ErrorCode Code;
        public readonly Severity Severity;
        public int Line, Col;
        public int LineEnd, ColEnd;
        public string Message;

        public Error(ErrorCode code, int line, int col, string message)
        {
            Code = code;
            Severity = Severity.Error;
            Line = line;
            Col = col;
            Message = message;
            LineEnd = line;
            ColEnd = col + 1;
        }

        public Error(ErrorCode code, TextSpan loc, string message, (int, int) offset,
            Severity severity = Severity.Error)
        {
            // Deliberately NOT routed through FileRange here: FileRange's convention is 0-based on
            // both axes (for the LSP/engine), but Error.Line/Col has always been 1-based line /
            // 0-based column -- the ANTLR IToken convention the original AddError/AddWarning calls
            // read straight off `loc.Start.Line`/`loc.Start.Column` with no adjustment. Superpower's
            // Position is 1-based on both axes, so only Column needs a "-1" here to match.
            Code = code;
            Severity = severity;
            var end = EndPosition(loc);
            Line = loc.Position.Line + offset.Item1;
            Col = loc.Position.Column - 1 + offset.Item2;
            Message = message;
            LineEnd = end.Line + offset.Item1;
            ColEnd = end.Column - 1 + offset.Item2;
        }

        static Position EndPosition(TextSpan span)
        {
            var pos = span.Position;
            foreach (var c in span.ToStringValue())
                pos = pos.Advance(c);
            return pos;
        }

        public override string ToString() => $"M{(int) Code}: {Severity} {Code} {Line}:{Col}: {Message}";
    }

    public static IValue? ParseExpr(AstVisitor visitor, string s, int offsetLine, int offsetColumn,
        out List<Error> errors)
    {
        var prevOffset = visitor.Offset;
        visitor.Offset = (offsetLine, offsetColumn);
        var tokenized = MoiraiTokenizer.Tokenize(s);
        foreach (var e in tokenized.Errors)
            visitor.Errors.Add(new Error(ErrorCode.Lexer, e.Position.Line + offsetLine,
                e.Position.Column - 1 + offsetColumn, e.Message));

        IValue? result = null;
        var parsed = MoiraiGrammar.TryParseExpr(tokenized.ParseTokens);
        if (!parsed.HasValue)
            visitor.Errors.Add(MakeParseError(parsed.ErrorPosition, s, parsed.ErrorMessage,
                parsed.Expectations, EndOf(tokenized.ParseTokens.ToArray()), offsetLine, offsetColumn));
        else
            result = visitor.ParseExpr(parsed.Value);

        errors = visitor.Errors;
        visitor.Offset = prevOffset;
        return result;
    }

    /// Everything a tool needs from one parse: the full token list (trivia included), the AST of
    /// every definition that survived chunked error recovery, the built Database, the AstVisitor
    /// (for InfoMarkers), and the errors. <see cref="Parse"/> is this with all but the Database and
    /// the errors discarded.
    public sealed record ToolingParse(
        MoiraiTokenizerResult Tokens,
        DefNode[] Defs,
        Database Database,
        AstVisitor Visitor,
        List<Error> Errors);

    /// Parse entry point for tooling -- the language server, which needs source positions and the
    /// tree, not just the built world. Replaces the ANTLR snapshot's SetupParser/IVisitor pair:
    /// there the caller drove the parser and accepted visitors over the tree itself; here the
    /// pipeline stays owned by the parser and hands back its intermediate products.
    ///
    /// The linker arrives as a factory rather than an instance because building one may require the
    /// Database to already exist -- the LSP's SourceLinker seeds itself with the builtin types and
    /// functions, reading them off Database.Instance, which is only set once the Database is
    /// constructed. Taking a factory makes that ordering the API's problem instead of every
    /// caller's.
    public static ToolingParse ParseForTooling(string s, Func<Database, ILinker>? createLinker = null)
    {
        var db = new Database();
        var visitor = new AstVisitor(db) { Linker = createLinker?.Invoke(db) };
        var tokenized = MoiraiTokenizer.Tokenize(s);
        foreach (var e in tokenized.Errors)
            visitor.Errors.Add(new Error(ErrorCode.Lexer, e.Position.Line, e.Position.Column - 1, e.Message));

        // Chunked at top-level def boundaries (Phase 4 of the migration plan): a syntax error in one
        // def must not blank out every definition in the file, and the LSP needs one diagnostic per
        // broken def, not just the first. Well-formed input always chunks to exactly one piece
        // covering the whole file, so this is a no-op for anything that already parses cleanly.
        var allDefs = new List<DefNode>();
        foreach (var chunk in ChunkTokens(tokenized.ParseTokens.ToArray()))
        {
            var chunkTokens = new TokenList<MoiraiTokenKind>(chunk);
            var parsed = MoiraiGrammar.TryParseR(chunkTokens);
            if (!parsed.HasValue)
            {
                visitor.Errors.Add(MakeParseError(parsed.ErrorPosition, s, parsed.ErrorMessage,
                    parsed.Expectations, EndOf(chunk), 0, 0));
                continue;
            }

            if (!parsed.Remainder.IsAtEnd)
            {
                var next = parsed.Remainder.ConsumeToken();
                visitor.Errors.Add(MakeParseError(next.Value.Position, s,
                    "unexpected content after the last definition", null, EndOf(chunk), 0, 0));
                continue;
            }

            allDefs.AddRange(parsed.Value.Defs);
        }

        if (allDefs.Count > 0)
            visitor.VisitR(new RNode(allDefs.ToArray(), allDefs[0].Span)); // RNode.Span is unused downstream

        return new ToolingParse(tokenized, allDefs.ToArray(), db, visitor, visitor.Errors);
    }

    public static Database Parse(string s, out List<Error> errors)
    {
        var parsed = ParseForTooling(s);
        errors = parsed.Errors;
        return parsed.Database;
    }

    static readonly MoiraiTokenKind[] TopLevelDefStartKinds =
    {
        MoiraiTokenKind.At, MoiraiTokenKind.Event, MoiraiTokenKind.Entity, MoiraiTokenKind.Singleton,
        MoiraiTokenKind.Trigger, MoiraiTokenKind.Enum, MoiraiTokenKind.Table, MoiraiTokenKind.Function,
    };

    /// Splits the (already trivia-filtered) parse token stream into independent chunks at top-level
    /// def boundaries. Deliberately column-based (a top-level keyword or `@` starting at column 1 —
    /// real .sg sources never indent top-level constructs) rather than brace-depth-based: a *broken*
    /// def is exactly the case chunking exists to isolate, and a missing `}` would leave a
    /// depth-tracking counter permanently elevated, silently swallowing every def for the rest of the
    /// file after the first mistake. `sawDefKeyword` keeps a run of `@attr` lines before a def from
    /// being sliced apart from the def they annotate (only a *complete* prior def — one that reached
    /// its own keyword, not just another attribute — licenses the next cut). Each chunk is parsed
    /// independently in <see cref="Parse"/> so one broken def doesn't take the rest of the file down
    /// with it.
    static List<Token<MoiraiTokenKind>[]> ChunkTokens(Token<MoiraiTokenKind>[] tokens)
    {
        var chunks = new List<Token<MoiraiTokenKind>[]>();
        int start = 0;
        bool sawDefKeyword = false;
        for (int i = 0; i < tokens.Length; i++)
        {
            bool atColumn1TopLevel = tokens[i].Span.Position.Column == 1 &&
                Array.IndexOf(TopLevelDefStartKinds, tokens[i].Kind) >= 0;

            if (i > start && atColumn1TopLevel && sawDefKeyword)
            {
                chunks.Add(tokens[start..i]);
                start = i;
                sawDefKeyword = false;
            }

            if (atColumn1TopLevel && tokens[i].Kind != MoiraiTokenKind.At)
                sawDefKeyword = true;
        }

        if (start < tokens.Length)
            chunks.Add(tokens[start..]);
        return chunks;
    }

    static Error MakeParseError(Position pos, string source, string? message, string[]? expectations,
        Position fallback, int offsetLine, int offsetColumn)
    {
        // Superpower reports Position.Empty when the parse ran out of input, which used to degrade
        // to line 1 column 1 -- putting the squiggle at the top of the file while you are typing at
        // the bottom. Fall back to the end of the last token we did consume.
        if (!pos.HasValue)
            pos = fallback;

        // Error.Line/Col is 1-based line / 0-based column (see the TextSpan Error constructor's
        // comment) -- Superpower's Position is 1-based on both axes, so only Column gets a "-1".
        int line = (pos.HasValue ? pos.Line : 1) + offsetLine;
        int col = (pos.HasValue ? pos.Column : 1) - 1 + offsetColumn;
        var text = message ?? "syntax error near " + Near(source, pos);
        if (expectations is { Length: > 0 })
            text += " (expected " + string.Join(", ", expectations.Distinct()) + ")";
        return new Error(ErrorCode.Parser, line, col, text);
    }

    /// One position past the last token of a chunk -- where a "ran out of input" syntax error
    /// belongs.
    static Position EndOf(Token<MoiraiTokenKind>[] chunk)
    {
        if (chunk.Length == 0)
            return Position.Empty;
        var span = chunk[^1].Span;
        var pos = span.Position;
        foreach (var c in span.ToStringValue())
            pos = pos.Advance(c);
        return pos;
    }

    static string Near(string source, Position pos)
    {
        if (!pos.HasValue) return "(end of input)";
        int start = Math.Max(0, pos.Absolute - 20);
        int len = Math.Min(40, source.Length - start);
        return source.Substring(start, len).Replace("\n", "\\n");
    }

    public interface ILinker
    {
        void DeclareType(FileRange range, EntityTypeId typeId, string? lineDefinition = null);
        void DeclareTypeProperty(FileRange range, PropertyId propertyDefinitionPropertyId, string? lineDefinition = null);
        void LinkType(FileRange range, EntityTypeId entityType, bool isDeclaration = false);
        void LinkProperty(FileRange range, PropertyId propertyId, bool isDeclaration = false);
        void DeclareEnum(FileRange range, EnumDefinitionId enumId);
        void LinkEnum(FileRange range, EnumDefinitionId enumId, bool isDeclaration = false);
        void LinkEnumMember(FileRange range, PropertyValue enumValue, bool isDeclaration = false);
        void LinkVariable(FileRange varId, AstVisitor.VariableDeclaration decl);
        void DeclareVariable(FileRange range, AstVisitor.VariableDeclaration variableDeclaration,
            FileRange variableScope);
        void DeclareFunction(FileRange fileRange, IFunctionDescriptor descriptor, string? inlineDef = null);
        void LinkFunction(FileRange range, IFunctionDescriptor descriptor);
        void DeclareTable(FileRange range, Moirai.Core.TableDefinition table);
        void LinkTable(FileRange range, Moirai.Core.TableDefinition table, bool isDeclaration = false);
    }

    internal struct PathParser(AstVisitor astVisitor, PathNode context)
    {
        internal void Rec(ref PropertyPath path, int idIndex,
            EntityType owningType, out PropertyValue.ValueType type)
        {
            var dotPropertyNode = idIndex < context.DotProperties.Length ? context.DotProperties[idIndex] : null;
            var propId = dotPropertyNode?.Property;

            type = default;
            if (propId != null)
            {
                ParseProperty(ref path, propId.Value, owningType, out type);
            }
            else if (dotPropertyNode?.Call != null)
            {
                // if we rewrite the calls to desugar the instance methods:
                // a.b.f() -> f(a.b)
                // a.f().b -> f(a).b
                // a.f().g() -> g(f(a))

                var funcName = dotPropertyNode.Call.FunId.Text;
                if (owningType.GetFunctionDefinition(funcName, out var fd))
                {
                    var ctx = new FunctionParseContext(astVisitor, dotPropertyNode.Call, fd, path);
                    var call = astVisitor.ParseUserFunctionCall(astVisitor, ctx, out type);
                    path = new PropertyPath(-1, PropertyValue.ValueType.Null);
                    path.AddCall(call);
                    astVisitor.Linker?.LinkFunction(new FileRange(dotPropertyNode.Call.FunId.Span),
                        new UserFunctionDescriptor(fd));
                }
            }

            if (idIndex + 1 < context.DotProperties.Length)
                Rec(ref path, idIndex + 1, astVisitor.Database.GetEntityType(type)!, out type);
        }

        public void ParseProperty(ref PropertyPath path, Ident rootProp, EntityType owningType, out PropertyValue.ValueType type)
        {
            string propertyName = rootProp.Text;
            var propertyId = owningType.GetPropertyId(propertyName);
            if (!propertyId.IsValid)
            {
                type = default;
                astVisitor.AddError(ErrorCode.UnknownProperty, rootProp.Span, propertyName);
                return;
            }

            // `id` is declared once for every type as an untyped ref; read through a type, it is a
            // reference to that type, so it can be passed where a `Person` is expected.
            type = propertyId == Database.PropId ? owningType.RefType : owningType.GetPropertyType(propertyName);
            astVisitor.Linker?.LinkProperty(new FileRange(rootProp.Span), propertyId);
            path.AddProperty(propertyId);
        }
    }
}
