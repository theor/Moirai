using System.Diagnostics.CodeAnalysis;
using Moirai.Parser.Ast;
using Superpower.Model;

namespace Moirai.Parser;

public static class StoryParser
{
    /// Built-ins that are gone, and what to write instead: a story that still uses one is told how to move
    /// on, rather than that the name is unknown.
    public static readonly IReadOnlyDictionary<string, string> Removed = new Dictionary<string, string>
    {
        ["call"] = "call() was removed: call an event or a function by name, name(args), and run one n times with repeat(n) { name() }",
    };

    public static bool GetFunctionDescriptor(string name, [NotNullWhen(true)] out FunctionDescriptor? descriptor)
    {
        descriptor = Functions.FirstOrDefault(f => f.FuncName == name);
        return descriptor != null;
    }

    /// `create ... { prop := value }`: sets properties on the new entity, and sees it.
    static readonly BlockSpec Initializer = new(BlockRuns.OnCreate, BlockSees.EnclosingAndVariable);

    public static readonly List<FunctionDescriptor> Functions =
    [
        new("create",
            [
                new BindingForm(BindingHead.Variable, VariableLives.Rest, FnReturn.Variable),
                new BindingForm(BindingHead.Variable, VariableLives.Rest, FnReturn.Variable).With(Initializer),
                new BindingForm(BindingHead.Name, VariableLives.Rest, FnReturn.Variable),
                new BindingForm(BindingHead.Name, VariableLives.Rest, FnReturn.Variable).With(Initializer),
            ],
            call => new CreateEntity(call.Variable, call.EntityType, call.Count == 0 ? null : call.Text(0), call.Block()),
            new FunctionDoc(DocCategory.Entities,
                "Creates a new T and binds it to $v, which stays in scope for the rest of the rule. The name is an interpolated string. The block sets properties on the new entity, one `prop := value` per line, before anything else sees it. Creating a singleton that already exists binds the existing one instead of making a second.",
                "create Person $p: '{roll(Name)}' {\n    age := 0\n    alive := true\n}")),
        new("each",
            [new BindingForm(BindingHead.Predicate, VariableLives.Block, FnReturn.Variable)
                .With(new BlockSpec(BlockRuns.PerMatch, BlockSees.EnclosingAndVariable))],
            call => new AssignPick(call.EntityType, call.Variable, call.Predicate, CallType.Each, call.Block()),
            new FunctionDoc(DocCategory.Queries,
                "Runs the block once for every T the predicate matches, with $v bound to each in turn, oldest entity first; with no predicate, for every T. The matches are found before the block first runs, so entities it creates are not visited. A statement in the block that stops (a failed pick) ends only that iteration. `each` never fails, even when nothing matches.",
                "each Person $p: (alive, age > 60) {\n    set $p.wealth = $p.wealth + 1\n}")),
        new("pick",
            [
                new BindingForm(BindingHead.Predicate, VariableLives.Rest, FnReturn.Variable),
                new BindingForm(BindingHead.Predicate, VariableLives.Rest, FnReturn.Variable)
                    .With(new BlockSpec(BlockRuns.OnMiss, BlockSees.EnclosingNotVariable, "else")),
            ],
            call => new AssignPick(call.EntityType, call.Variable, call.Predicate, CallType.Pick, elseEffects: call.Block("else")),
            new FunctionDoc(DocCategory.Queries,
                "Picks one T the predicate matches, uniformly at random, and binds it to $v; with no predicate, any T. When nothing matches, the rule stops there and has failed: what it already did stays in the world, but its changes are not logged and no trigger sees them, so pick before changing anything. With `else`, the block runs instead and the rule then stops *successfully*: its changes are logged and triggers see them. As a condition, `if (pick T $v: (...)) { }` tests whether anything matched.",
                "pick Person $p: (alive, partner = null) else {\n    record('Nobody is left to marry')\n}\nrecord('{$p.name} stays single')")),

        new("schedule",
            [P.Call(FnReturn.Nothing, P.Entity("entity"), P.Number("year")).With(new BlockSpec(BlockRuns.Later, BlockSees.OnlySelf))],
            call =>
            {
                // The block runs later via Database.RunAction, so wrap it as a standalone EventTrigger (not added
                // to Actions/Triggers, so it never auto-fires). The high id base keeps schedule sites from
                // colliding with real event ids in the profiler's per-id stats table.
                var line = call.Context.CallContext.Span.Position.Line;
                var body = call.Block()!;
                var site = new EventTrigger(1_000_000 + call.Context.Visitor.Database.ScheduleSiteCount, $"schedule@{line}", false, null)
                {
                    DebugScopeRoot = call.BlockScope(),
                    Line = line,
                    IsScheduled = true,
                    RngKey = (string) call.Prepared!,
                };
                site.Effects.AddRange(body);
                var siteIndex = call.Context.Visitor.Database.RegisterScheduleSite(site, call.SelfVariable);
                return new ScheduleEffect(call.Value(0), call.Value(1), siteIndex, body);
            },
            new FunctionDoc(DocCategory.Rules,
                "Defers the block until the simulation reaches `year`, then runs it once, as a rule of its own, with $self bound to `entity`. Both arguments are evaluated now. The block sees $self but none of the enclosing rule's locals, so read what it needs from $self. A year that is not in the future fires next year. If the entity no longer exists by then, nothing runs, so a block that cares whether $self is still alive should test it.",
                "pick Person $p: (alive)\nschedule($p, #Time.year + 20) {\n    if $self.alive {\n        set $self.age = $self.age + 20\n    }\n}"),
            // Taken before the block is parsed, so a schedule nested in this one is numbered after it.
            prepare: ctx => ctx.Visitor.NextScheduleStreamKey()),
        new("assert", [P.Call(FnReturn.Nothing, P.Condition("condition"))],
            call => new AssertInstr(call.Value(0), call.Context.GetText(call.Node(0)!.Span)),
            new FunctionDoc(DocCategory.Testing,
                "Stops the simulation with an error when the condition is false. The error quotes the condition as written.",
                "pick Person $p: (alive)\nassert($p.age >= 0)")),
        new("assert_eq", [P.Call(FnReturn.Nothing, P.Any("actual"), P.Any("expected"))],
            call => new AssertInstr(call.Value(0), call.Value(1),
                $"{call.Context.GetText(call.Node(0)!.Span)} = {call.Context.GetText(call.Node(1)!.Span)}"),
            new FunctionDoc(DocCategory.Testing,
                "Stops the simulation with an error when the two values differ. The error quotes both expressions as written and prints both values.",
                "create Person $p: 'Ada' {\n    age := 3\n}\nassert_eq($p.age, 3)")),
        new("mark", [P.Call(FnReturn.Nothing, P.Entity("entity"))],
            call => CurrentRule(call, "mark") is { } rule ? new Mark(call.Value(0), rule) : null,
            new FunctionDoc(DocCategory.Rules,
                "Remembers that this rule touched `entity` this year, for `since_last` to read. Marks belong to the rule that makes them, so pair `mark` and `since_last` in the same rule, which must be an event or a trigger. Use the pair to keep a rule from picking the same entity again too soon.",
                "pick Person $p: (alive, since_last($p) > 4)\nmark($p)\nrecord('{$p.name} goes on a pilgrimage')")),
        new("since_last", [P.Call(FnReturn.Number, P.Entity("entity"))],
            call => CurrentRule(call, "since_last") is { } rule ? new SinceLast(call.Value(0), rule) : null,
            new FunctionDoc(DocCategory.Rules,
                "The number of years since this rule last called `mark(entity)`. An entity this rule never marked counts as marked in year 0, so it reads as long ago rather than as a special value. Usable in a pick's predicate.",
                "pick Person $p: (alive, since_last($p) > 10)\nmark($p)")),
        new("related", [P.Call(FnReturn.Bool, P.Entity("a"), P.Entity("b"), P.IntLiteral("n", 0, Related.MaxDegree))],
            call =>
            {
                // Parents are resolved here, once, from the first argument's type's parent roles
                // (@parents, or parent1/parent2 by default) -- the same ones the viewer's family tree reads.
                var aType = call.Type(0);
                var type = aType.IsRefType && aType.Index != 0
                    ? call.Context.Visitor.Database.GetEntityType(new EntityTypeId(aType.Index))
                    : null;
                var p1 = type?.Role(EntityRole.Parent1) ?? default;
                var p2 = type?.Role(EntityRole.Parent2) ?? default;
                if (!p1.IsValid || !p2.IsValid)
                {
                    call.Context.Visitor.AddError(ErrorCode.UnknownProperty, call.Node(0)?.Span ?? call.Context.CallContext.Span,
                        "related() needs an entity whose type has parents: declare parent1 and parent2, or name them with @parents(a, b)");
                    return null;
                }

                return new Related(call.Value(0), call.Value(1), call.Int(2), p1, p2);
            },
            new FunctionDoc(DocCategory.Kinship,
                "True when a and b share an ancestor within n degrees of kinship, counted the civil-law way: parent 1, grandparent or sibling 2, aunt or uncle 3, first cousin 4. The parents are the type's `@parents`, or `parent1`/`parent2` by default, and the type must have them.",
                "pick Person $x: (alive, partner = null)\npick Person $y: (alive, partner = null, $y != $x, not(related($x, $y, 4)))")),
        new("record", [P.Call(FnReturn.Nothing, P.Text("text"), P.Number("weight").Optional())],
            call => new Record(call.Text(0), call.Count > 1 ? call.Value(1) : null),
            new FunctionDoc(DocCategory.Records,
                "Writes a sentence into the world's history. The text is interpolated: `{$p.name}` inserts a value, and an entity mentioned this way becomes a participant of the record, so it links to that entity and shows on its Life page. The optional weight (taken as a whole number) says how much the record matters: 1 by default, 0 for background noise, higher for the turning points the chronicle surfaces. The weight is metadata only and never changes how the world runs.",
                "pick Person $p: (alive)\nrecord('{$p.name} is crowned', 5)")),
        new("link", [P.Call(FnReturn.String, P.Entity("entity"), P.String("text"))],
            call => new InterpolatedStringLink(call.Value(0), call.Value(1)),
            new FunctionDoc(DocCategory.Records,
                "Inside a record's text, shows `text` as a link to `entity`. Use it when the words should not be the entity's name. Unlike `{$p.name}`, a link does not make the entity one of the record's participants.",
                "pick Person $p: (alive)\nrecord('{$p.name} paints a {link($p, 'self-portrait')}')")),

        new("random",
            [
                P.Call(FnReturn.EnumOfArg, P.EnumType("E")),
                P.Call(FnReturn.Number, P.Number("max")),
                P.Call(FnReturn.Number, P.Number("min"), P.Number("max")),
            ],
            call => call.FormIndex switch
            {
                0 => new RandomEnum(call.Enum(0)),
                1 => new RandomRange(new Literal(0), call.Value(0)),
                _ => new RandomRange(call.Value(0), call.Value(1)),
            },
            new FunctionDoc(DocCategory.Randomness,
                "`random(E)` is one of the enum's values, each equally likely. `random(max)` is a whole number from 0 to max - 1, and `random(min, max)` one from min to max - 1: the upper bound is never drawn. The bounds can be any number expressions; one that is not whole is cut to its whole part. When max is not above min, the result is min. Every call draws from the rule's own random stream, so the world stays the same for a given seed.",
                "pick Person $x: (alive)\nset $x.job = random(Job)\nif random(100) < 8 {\n    set $x.wealth = random(40, 90)\n}\nvar $lucky: random(count Person $p: (alive))")),
        new("chance",
            [
                P.Call(FnReturn.Bool, P.Number("p")),
                P.Call(FnReturn.Nothing, P.Number("p")).With(new BlockSpec(BlockRuns.OnHit, BlockSees.Enclosing)),
            ],
            call =>
            {
                // A query's narrowing decides which candidates its predicate is evaluated on, so a draw
                // inside one would make the world depend on the index -- pick first, then roll.
                if (call.Context.Visitor.InSqlPredicate)
                    call.Context.Visitor.AddError(ErrorCode.InvalidArgument, call.Context.CallContext.Span,
                        "chance() cannot be part of a pick/each predicate; pick first, then test chance()");
                return new Chance(call.Value(0), call.Block());
            },
            new FunctionDoc(DocCategory.Randomness,
                "True with probability p, a percentage: `chance(3%)`. It always draws exactly once, whatever p is. With a block, it is a statement: the block runs when the draw hits, and the rule carries on either way. It cannot be part of a pick or each predicate: pick first, then test chance.",
                "pick Person $p: (alive)\nchance(5%) {\n    record('{$p.name} finds a fortune')\n}\nif chance(50%) {\n    set $p.wealth = $p.wealth + 1\n}")),
        new("repeat", [P.Call(FnReturn.Nothing, P.Number("n")).With(new BlockSpec(BlockRuns.Times, BlockSees.Enclosing))],
            call => new Repeat(call.Value(0), call.Block()!),
            new FunctionDoc(DocCategory.Rules,
                "Runs the block n times; n is read once, before the first turn. Each turn is a scope of its own, and a statement that stops (a failed pick) ends that turn only, so calling an event n times this way is n separate runs of it. Draws no random numbers of its own.",
                "repeat(3) {\n    harvest()\n}\nrepeat(count Person $p: (alive)) {\n    record('A lantern is lit')\n}")),
        new("roll", [P.Call(FnReturn.TableEntryOfArg, P.Table("T"))],
            call => new RollTable(call.Table(0).Id, call.Table(0).Name),
            new FunctionDoc(DocCategory.Randomness,
                "Draws one entry from a `table`, according to its weights.",
                "create Person $p: '{roll(Name)}'")),
        new("add", [P.Call(FnReturn.Nothing, P.Collection("coll"), P.ElementOf("value", 0))],
            call => CollectionCall(call, (full, owner, coll) => new CollectionMutate(full, owner, coll, call.Value(1), true)),
            new FunctionDoc(DocCategory.Collections,
                "Adds a value to a collection property (`prop friends: [Person]`). A collection is a set: adding a value it already holds changes nothing.",
                "pick Person $a: (alive)\npick Person $b: (alive, $b != $a)\nadd($a.friends, $b)")),
        new("remove", [P.Call(FnReturn.Nothing, P.Collection("coll"), P.ElementOf("value", 0))],
            call => CollectionCall(call, (full, owner, coll) => new CollectionMutate(full, owner, coll, call.Value(1), false)),
            new FunctionDoc(DocCategory.Collections,
                "Removes a value from a collection property. Removing a value it does not hold changes nothing.",
                "pick Person $a: (alive)\npick Person $b: (contains($a.friends, $b))\nremove($a.friends, $b)")),
        new("contains", [P.Call(FnReturn.Bool, P.Collection("coll"), P.ElementOf("value", 0))],
            call => CollectionCall(call, (full, owner, coll) =>
                new CollectionQuery(CollectionQuery.QueryKind.Contains, full, owner, coll, call.Value(1))),
            new FunctionDoc(DocCategory.Collections,
                "True when the collection property holds the value. Usable in a pick's predicate.",
                "pick Person $a: (alive)\npick Person $b: (alive, $b != $a, not(contains($a.friends, $b)))")),
        new("sum", [new BindingForm(BindingHead.PredicateAndValue, VariableLives.Call, FnReturn.Number) { Value = P.Number("value") }],
            call => AggregateCall(call, Aggregate.AggregateKind.Sum),
            new FunctionDoc(DocCategory.Queries,
                "The total of `value` over every T the predicate matches, 0 when none does. The arguments before the last are the predicate, joined by `and` like a pick's. $v exists only inside the call. A sum of percentages is a float, since it can pass 100. Draws no random numbers of its own, and can sit inside another query's predicate.",
                "var $total: sum Person $p: (alive, $p.wealth)")),
        new("avg", [new BindingForm(BindingHead.PredicateAndValue, VariableLives.Call, FnReturn.Number) { Value = P.Number("value") }],
            call => AggregateCall(call, Aggregate.AggregateKind.Avg),
            new FunctionDoc(DocCategory.Queries,
                "The mean of `value` over every T the predicate matches, 0 when none does: use `count` to tell none from zero. The mean of whole numbers is a float. Written like `sum`.",
                "var $mean: avg Person $p: (alive, $p.happiness)")),
        new("min", [new BindingForm(BindingHead.PredicateAndValue, VariableLives.Call, FnReturn.Number) { Value = P.Number("value") }],
            call => AggregateCall(call, Aggregate.AggregateKind.Min),
            new FunctionDoc(DocCategory.Queries,
                "The smallest `value` over every T the predicate matches, 0 when none does. Written like `sum`.",
                "var $youngest: min Person $p: (alive, $p.age)")),
        new("max", [new BindingForm(BindingHead.PredicateAndValue, VariableLives.Call, FnReturn.Number) { Value = P.Number("value") }],
            call => AggregateCall(call, Aggregate.AggregateKind.Max),
            new FunctionDoc(DocCategory.Queries,
                "The largest `value` over every T the predicate matches, 0 when none does. Written like `sum`.",
                "var $oldest: max Person $p: (alive, $p.age)")),
        new("count",
            [
                new BindingForm(BindingHead.Predicate, VariableLives.Call, FnReturn.Number),
                P.Call(FnReturn.Number, P.Collection("coll")),
            ],
            call => call.Form is BindingForm
                ? new Aggregate(Aggregate.AggregateKind.Count, call.EntityType, call.Variable,
                    call.PredicateCount == 0 ? null : call.Predicate, null, PropertyValue.TypeNumber)
                : CollectionCall(call, (full, owner, coll) =>
                    new CollectionQuery(CollectionQuery.QueryKind.Count, full, owner, coll, null)),
            new FunctionDoc(DocCategory.Queries,
                "`count T $v: (predicate)` is how many T the predicate matches; with no predicate, how many T exist at all. $v exists only inside the call. `count(coll)` is the number of values in a collection property. Draws no random numbers, and can sit inside another query's predicate.",
                "var $living: count Person $p: (alive)\npick Person $p: (alive, count($p.friends) < 3)")),
        new("not", [P.Call(FnReturn.Bool, P.Condition("condition"))],
            call => new MathUnary(MathUnary.UnaryFunction.Not, call.Value(0)),
            new FunctionDoc(DocCategory.Math,
                "True when the condition is false. Usable in a pick's predicate.",
                "pick Person $p: (not($p.alive))")),
        new("floor", [P.Call(FnReturn.Number, P.Number("x"))],
            call => new MathUnary(MathUnary.UnaryFunction.Floor, call.Value(0)),
            new FunctionDoc(DocCategory.Math,
                "x rounded down to a whole number.",
                "var $half: floor(count Person $p: (alive) / 2)")),
        new("round", [P.Call(FnReturn.Number, P.Number("x"))],
            call => new MathUnary(MathUnary.UnaryFunction.Round, call.Value(0)),
            new FunctionDoc(DocCategory.Math,
                "x rounded to the nearest whole number. A half rounds to the even neighbour: round(2.5) is 2, round(3.5) is 4.",
                "var $mean: round(avg Person $p: (alive, $p.age))")),
        new("ceiling", [P.Call(FnReturn.Number, P.Number("x"))],
            call => new MathUnary(MathUnary.UnaryFunction.Ceiling, call.Value(0)),
            new FunctionDoc(DocCategory.Math,
                "x rounded up to a whole number.",
                "var $boats: ceiling(count Person $p: (alive) / 12)")),
        new("clamp01", [P.Call(FnReturn.Number, P.Number("x"))],
            call => new MathUnary(MathUnary.UnaryFunction.Clamp01, call.Value(0)),
            new FunctionDoc(DocCategory.Math,
                "x limited to the range 0 to 1. It is for fractions: a percentage is held as 0 to 100 and is already kept in that range whenever it is set.",
                "var $share: clamp01(count Person $p: (alive) / 1000)")),
        new("debug", [P.Call(FnReturn.Nothing, P.Any("value").Repeated())],
            call => new DebugPrint(Enumerable.Range(0, call.Count).Select(call.Value)),
            new FunctionDoc(DocCategory.Testing,
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

    /// The id of the event or trigger being parsed, which `mark` and `since_last` key their marks by. A
    /// function body has none -- it can run inside any rule -- so a mark there is an error rather than the
    /// crash it used to be.
    static int? CurrentRule(BoundCall call, string name)
    {
        if (call.Context.Visitor.CurrentEventTrigger is { } rule)
            return rule.Id;
        call.Context.Visitor.AddError(ErrorCode.InvalidArgument, call.Context.CallContext.Span,
            $"{name}() works only in an event or a trigger: its marks belong to the rule that makes them");
        return null;
    }

    /// A call to an event, `name(args...)`: the arguments from <paramref name="firstArgument"/> on are the
    /// event's parameters, each checked against the type it declares. An event runs as a rule of its own
    /// (see CallRule), so the call is a statement: it has no value.
    internal static IValueCall EventCall(FunctionParseContext ctx, int eventIndex, int firstArgument, string label)
    {
        var visitor = ctx.Visitor;
        var pars = visitor.Database.Actions[eventIndex].Parameters ?? [];
        var given = ctx.ArgCount - firstArgument;
        if (given > pars.Count)
            visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext.Span,
                $"{label} takes {pars.Count} argument{(pars.Count == 1 ? "" : "s")}, got {given}");

        var args = new IValue[pars.Count];
        for (int i = 0; i < pars.Count; i++)
        {
            if (i >= given)
            {
                visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext.Span,
                    $"{label} is missing argument {pars[i].ParamName}: {visitor.Database.Printer.Print(pars[i].ParamType)}");
                args[i] = new Literal(0);
                continue;
            }

            var value = ctx.ParseArgument(firstArgument + i, out var type);
            if (!AstVisitor.Accepts(pars[i].ParamType, type))
                visitor.AddError(ErrorCode.MismatchedAssignmentTypes,
                    ctx.GetArgumentToken(firstArgument + i)?.Span ?? ctx.CallContext.Span,
                    $"Expected {visitor.Database.Printer.Print(pars[i].ParamType)} got {visitor.Database.Printer.Print(type)}");
            args[i] = value;
        }

        return pars.Count == 0 ? new CallRule(eventIndex, 1) : new CallRule(eventIndex, args);
    }

    static IValueCall CollectionCall(BoundCall call, Func<PropertyPath, PropertyPath, PropertyId, IValueCall> make)
    {
        var (full, owner, coll) = call.Collection(0);
        return make(full, owner, coll);
    }

    /// `sum|avg|min|max T $v: (pred..., value)`, bound: the result's type follows the value's -- a sum of
    /// percentages runs past 100 so it is a float, and so is the mean of whole numbers.
    static IValueCall AggregateCall(BoundCall call, Aggregate.AggregateKind kind)
    {
        var value = call.Count - 1;
        var valueType = call.Type(value);
        var resultType = (kind, valueType.BaseType) switch
        {
            (Aggregate.AggregateKind.Sum, PropertyValue.ValueBaseType.Percentage) => PropertyValue.TypeFloat,
            (Aggregate.AggregateKind.Avg, PropertyValue.ValueBaseType.Number) => PropertyValue.TypeFloat,
            _ => valueType,
        };
        call.ReturnType = resultType;
        return new Aggregate(kind, call.EntityType, call.Variable, call.PredicateCount == 0 ? null : call.Predicate,
            call.Value(value), resultType);
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
        /// An event, at its name: registered before any body is parsed, so a call can link to it wherever
        /// either is written.
        void DeclareEvent(FileRange range, EventTrigger rule);
        /// A call to an event by name, `harvest()`.
        void LinkEvent(FileRange range, EventTrigger rule);
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
