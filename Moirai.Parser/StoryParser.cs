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
        }),
        new("each", true,
            ctx =>
            {
                var scopeContext = ctx.GetScopeContext();
                using var vs = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, scopeContext?.Span);
                var variableIndex = ctx.ParseVariable(out var etid, out _);
                return (new AssignPick(etid, variableIndex, ctx.ParsePredicateSql(etid),
                        CallType.Each, ctx.Visitor.ParseRawScope(scopeContext, out _)),
                    PropertyValue.TypeTypedRef(etid));
            }),
        new("pick", true,
            ctx =>
            {
                var variableIndex = ctx.ParseVariable(out var etid, out _);
                return (new AssignPick(etid, variableIndex, ctx.ParsePredicateSql(etid),
                        CallType.Pick),
                    PropertyValue.TypeTypedRef(etid));
            }),

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
            };
            site.Effects.AddRange(body);
            var siteIndex = ctx.Visitor.Database.RegisterScheduleSite(site, selfVarIndex);

            return (new ScheduleEffect(entity, year, siteIndex, body), PropertyValue.ValueType.Null);
        }),
        new("assert", false, ctx =>
            (new AssertInstr(ctx.ParseArgument(0), ctx.GetText(ctx.GetArgumentToken(0)!.Span)),
                PropertyValue.ValueType.Null)),
        new("assert_eq", false, ctx =>
            (new AssertInstr(
                    ctx.ParseArgument(0),
                    ctx.ParseArgument(1),
                    $"{ctx.GetText(ctx.GetArgumentToken(0)!.Span)} = {ctx.GetText(ctx.GetArgumentToken(1)!.Span)}"),
                PropertyValue.ValueType.Null)),
        new("mark", false, ctx =>
        {
            ctx.ExpectArgcount(1);
            var e = ctx.ParseArgument(0);
            return (new Mark(e, ctx.Visitor.CurrentEventTrigger!.Id), PropertyValue.ValueType.Null);
        }),
        new("since_last", false, ctx =>
        {
            ctx.ExpectArgcount(1);
            var e = ctx.ParseArgument(0);
            return (new SinceLast(e, ctx.Visitor.CurrentEventTrigger!.Id), PropertyValue.TypeNumber);
        }),
        new("record", false, ctx =>
        {
            var interpolatedString = (InterpolatedString) ctx.ParseArgument(0);
            return (new Record(interpolatedString), PropertyValue.ValueType.Null);
        },
        "Records a string into the world history"),
        new("link", false, ctx =>
        {
            var linkValue = ctx.ParseArgument(0);
            var linkText = ctx.ParseArgument(1);
            return (new InterpolatedStringLink(linkValue, linkText), PropertyValue.TypeString);
        }),
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
                        if (at != pars[i].ParamType)
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
        }),

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
        },
            ""),
        new("roll", false, ctx =>
        {
            // roll(TableName) — sample a named weighted table. The arg is a bare table name (a
            // TYPE_ID), looked up by text rather than parsed as a value (it isn't an enum/entity).
            if (ctx.ArgCount != 1)
            {
                ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext.Span, "'roll' takes one table name");
                return (null!, PropertyValue.ValueType.Null);
            }

            var tableName = ctx.GetText(ctx.GetArgumentToken(0)!.Span);
            if (!ctx.Visitor.Database.GetTableDefinition(tableName, out var table))
            {
                ctx.Visitor.AddError(ErrorCode.UnknownTable, ctx.CallContext.Span, tableName);
                return (null!, PropertyValue.ValueType.Null);
            }

            return (new RollTable(table.Id, table.Name), table.ValueType);
        }, "Samples a named weighted table: roll(TableName)"),
        new("add", false, ctx =>
        {
            ctx.ExpectArgcount(2);
            if (ctx.ParseCollectionPath(0, out var full, out var owner, out var coll))
                return (new CollectionMutate(full, owner, coll, ctx.ParseArgument(1), true),
                    PropertyValue.ValueType.Null);
            return (null!, PropertyValue.ValueType.Null);
        }, "Adds a value to a collection property: add($e.coll, $x)"),
        new("remove", false, ctx =>
        {
            ctx.ExpectArgcount(2);
            if (ctx.ParseCollectionPath(0, out var full, out var owner, out var coll))
                return (new CollectionMutate(full, owner, coll, ctx.ParseArgument(1), false),
                    PropertyValue.ValueType.Null);
            return (null!, PropertyValue.ValueType.Null);
        }, "Removes a value from a collection property: remove($e.coll, $x)"),
        new("contains", false, ctx =>
        {
            ctx.ExpectArgcount(2);
            if (ctx.ParseCollectionPath(0, out var full, out var owner, out var coll))
                return (new CollectionQuery(CollectionQuery.QueryKind.Contains, full, owner, coll,
                    ctx.ParseArgument(1)), PropertyValue.TypeBool);
            return (null!, PropertyValue.TypeBool);
        }, "Tests collection membership: contains($e.coll, $x)"),
        new("count", false, ctx =>
        {
            ctx.ExpectArgcount(1);
            if (ctx.ParseCollectionPath(0, out var full, out var owner, out var coll))
                return (new CollectionQuery(CollectionQuery.QueryKind.Count, full, owner, coll, null),
                    PropertyValue.TypeNumber);
            return (null!, PropertyValue.TypeNumber);
        }, "Number of elements in a collection property: count($e.coll)"),
        new("not", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Not, ctx.ParseArgument(0)), PropertyValue.TypeBool)),
        new("floor", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Floor, ctx.ParseArgument(0)), PropertyValue.TypeNumber)),
        new("round", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Round, ctx.ParseArgument(0)), PropertyValue.TypeNumber)),
        new("ceiling", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Ceiling, ctx.ParseArgument(0)), PropertyValue.TypeNumber)),
        new("clamp01", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Clamp01, ctx.ParseArgument(0)), PropertyValue.TypeNumber)),
        new("debug", false,
            ctx => (
                new DebugPrint(Enumerable.Repeat((object?) null, ctx.ArgCount).Select((_, i) => ctx.ParseArgument(i))),
                PropertyValue.ValueType.Null))
    ];

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
            visitor.Errors.Add(MakeParseError(parsed.ErrorPosition, s, parsed.ErrorMessage, offsetLine, offsetColumn));
        else
            result = visitor.ParseExpr(parsed.Value);

        errors = visitor.Errors;
        visitor.Offset = prevOffset;
        return result;
    }

    public static Database Parse(string s, out List<Error> errors)
    {
        var db = new Database();
        var visitor = new AstVisitor(db);
        var tokenized = MoiraiTokenizer.Tokenize(s);
        foreach (var e in tokenized.Errors)
            visitor.Errors.Add(new Error(ErrorCode.Lexer, e.Position.Line, e.Position.Column - 1, e.Message));

        var parsed = MoiraiGrammar.TryParseR(tokenized.ParseTokens);
        if (!parsed.HasValue)
        {
            visitor.Errors.Add(MakeParseError(parsed.ErrorPosition, s, parsed.ErrorMessage, 0, 0));
        }
        else
        {
            if (!parsed.Remainder.IsAtEnd)
            {
                var next = parsed.Remainder.ConsumeToken();
                visitor.Errors.Add(MakeParseError(next.Value.Position, s,
                    "unexpected content after the last definition", 0, 0));
            }

            visitor.VisitR(parsed.Value);
        }

        errors = visitor.Errors;
        return db;
    }

    static Error MakeParseError(Position pos, string source, string? message, int offsetLine, int offsetColumn)
    {
        // Error.Line/Col is 1-based line / 0-based column (see the TextSpan Error constructor's
        // comment) -- Superpower's Position is 1-based on both axes, so only Column gets a "-1".
        int line = (pos.HasValue ? pos.Line : 1) + offsetLine;
        int col = (pos.HasValue ? pos.Column : 1) - 1 + offsetColumn;
        return new Error(ErrorCode.Parser, line, col, message ?? "syntax error near " + Near(source, pos));
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

            type = owningType.GetPropertyType(propertyName);
            astVisitor.Linker?.LinkProperty(new FileRange(rootProp.Span), propertyId);
            path.AddProperty(propertyId);
        }
    }
}
