using System.Diagnostics.CodeAnalysis;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

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
            return (
                new CreateEntity(ctx.ParseVariable(out var etid, out _), etid,
                    ctx.ArgCount == 0 ? null : (InterpolatedString) ctx.ParseArgument(0)),
                PropertyValue.TypeTypedRef(etid));
        }),
        new("each", true,
            ctx =>
            {
                var scopeContext = ctx.GetScopeContext();
                using var vs = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, scopeContext);
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

        new("assert", false, ctx =>
            (new AssertInstr(ctx.ParseArgument(0), ctx.GetText(ctx.GetArgumentToken(0))),
                PropertyValue.ValueType.Null)),
        new("assert_eq", false, ctx =>
            (new AssertInstr(
                    ctx.ParseArgument(0),
                    ctx.ParseArgument(1),
                    $"{ctx.GetText(ctx.GetArgumentToken(0))} = {ctx.GetText(ctx.GetArgumentToken(1))}"),
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
            string? eventName = arg is MoiraiParser.ExprContext e
                ? e.value()?.path()?.GetText() ?? e.value()?.@string()?.GetString()
                : (arg is MoiraiParser.ValueContext v)
                    ? v.path()?.GetText() ?? v.@string()?.GetText()
                    : null;
            if (eventName == null)
            {
                ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext, "event name");
                return (null!, PropertyValue.ValueType.Null);
            }

            var eventIndex = ctx.Visitor.Database.Actions.FindIndex(r => r.Name == eventName);
            if (eventIndex == -1)
            {
                ctx.Visitor.AddError(ErrorCode.UnknownRule, arg, eventName);
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

            // TODO type calls ?
            return (new CallRule(eventIndex, count), PropertyValue.ValueType.Null);
        }),

        new("random", false, ctx =>
        {
            var argCount = ctx.ArgCount;
            if (argCount == 0)
            {
                ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext,
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

            ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext, ctx.GetText(ctx.CallContext));
            return (null!, PropertyValue.ValueType.Null);
        },
            ""),
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
        MoiraiParser Parser { get; set; }
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
    }

    public struct Error
    {
        public readonly ErrorCode Code;
        public int Line, Col;
        public int LineEnd, ColEnd;
        public string Message;

        public Error(ErrorCode code, int line, int col, string message)
        {
            Code = code;
            Line = line;
            Col = col;
            Message = message;
            LineEnd = line;
            ColEnd = col + 1;
        }

        public Error(ErrorCode code, ITerminalNode loc, string message, (int, int) offset)
        {
            Code = code;
            Line = loc.Symbol.Line + offset.Item1;
            Col = loc.Symbol.Column + offset.Item2;
            Message = message;
            LineEnd = loc.Symbol.Line;
            ColEnd = loc.Symbol.Column + loc.Symbol.Text.Length;
        }

        public Error(ErrorCode code, ParserRuleContext loc, string message, (int, int) offset)
        {
            Code = code;
            Line = loc.Start.Line + offset.Item1;
            Col = loc.Start.Column + offset.Item2;
            Message = message;
            LineEnd = loc.Stop.Line + offset.Item1;
            ColEnd = loc.Stop.Column + offset.Item2;
        }

        public override string ToString() => $"M{(int) Code}: {Code} {Line}:{Col}: {Message}";
    }

    class Listener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
    {
        private readonly List<Error> _errors;
        private readonly (int offsetLine, int offsetColumn) _offset;

        public Listener(List<Error> errors, (int offsetLine, int offsetColumn)? offset)
        {
            _errors = errors;
            _offset = offset ?? (0, 0);
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(ErrorCode.Lexer, line + _offset.offsetLine, charPositionInLine + _offset.offsetColumn,
                "Lexer:" + msg));
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(ErrorCode.Parser, line + _offset.offsetLine,
                charPositionInLine + _offset.offsetColumn, "Parser:" + msg));
        }
    }

    public static IValue? ParseExpr(AstVisitor visitor, string s, int offsetLine, int offsetColumn,
        out List<Error> errors)
    {
        var prevOffset = visitor.Offset;
        SetupParser(s, out var parser, visitor, (offsetLine, offsetColumn));
        var r = parser.expr();
        var propertyPath = visitor.ParseExpr(r);
        errors = visitor.Errors;
        visitor.Offset = prevOffset;
        return propertyPath;
    }

    public static Database Parse(string s, out List<Error> errors)
    {
        var db = new Database();
        var visitor = new AstVisitor(db, null!);
        SetupParser(s, out var parser, visitor);
        var r = parser.r();
        r.Accept(visitor);
        errors = visitor.Errors;
        return db;
    }

    public static void SetupParser(string s, out MoiraiParser parser, IVisitor visitor,
        (int offsetLine, int offsetColumn)? offset = null, bool mergeChannels = false)
    {
        var fromString = new CodePointCharStream(s /*.TrimStart('\r', '\n', ' ')*/);
        var lexer = new moirai_lexer(fromString);
        var tokens = /*mergeChannels ? new BufferedTokenStream(lexer) :*/ new CommonTokenStream(lexer);
        parser = new MoiraiParser(tokens);
        visitor.Parser = parser;
        visitor.Offset = offset ?? (0, 0);
        var listener = new Listener(visitor.Errors, offset);
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);
    }

    public interface ILinker
    {
        void DeclareType(FileRange range, EntityTypeId typeId, string? lineDefinition = null);
        void DeclareTypeProperty(FileRange range, PropertyId propertyDefinitionPropertyId, string? lineDefinition = null);
        void LinkType(FileRange range, EntityTypeId entityType);
        void LinkProperty(FileRange range, PropertyId propertyId);
        void DeclareEnum(FileRange range, EnumDefinitionId enumId);
        void LinkEnum(FileRange range, EnumDefinitionId enumId);
        void LinkEnumMember(FileRange range, PropertyValue enumValue);
        void LinkVariable(FileRange varId, AstVisitor.VariableDeclaration decl);
        void DeclareVariable(FileRange range, AstVisitor.VariableDeclaration variableDeclaration,
            FileRange variableScope);
        void DeclareFunction(FileRange fileRange, IFunctionDescriptor descriptor, string? inlineDef = null);
        void LinkFunction(FileRange range, IFunctionDescriptor descriptor);
    }

    public static ITerminalNode GetTypeTerminal(MoiraiParser.TypeContext type)
    {
        return type.TYPE_ID() ?? type.ID();
    }

    internal struct PathParser(AstVisitor astVisitor, MoiraiParser.PathContext context)
    {
        internal void Rec(ref PropertyPath path, int idIndex,
            EntityType owningType, out PropertyValue.ValueType type)
        {
            var dotPropertyContext = context.dot_property(idIndex);
            var propId = dotPropertyContext?.property_id();

            type = default;
            if (propId != null)
            {
                ParseProperty(ref path, propId, owningType, out type);
            }
            else
            {
                // if we rewrite the calls to desugar the instance methods:
                // a.b.f() -> f(a.b)
                // a.f().b -> f(a).b
                // a.f().g() -> g(f(a))
                    
                var funcName = dotPropertyContext.call().fun_id().GetText();
                if(owningType.GetFunctionDefinition(funcName, out var fd))
                {
                    var ctx = new FunctionParseContext(astVisitor, dotPropertyContext.call(), fd, path);
                    var call = astVisitor.ParseUserFunctionCall(astVisitor, ctx, out type);
                    path = new PropertyPath(-1, PropertyValue.ValueType.Null);
                    path.AddCall(call);
                    astVisitor.Linker?.LinkFunction(dotPropertyContext.call().fun_id(),new UserFunctionDescriptor(fd));
                }
            }

            if (context.dot_property() != null && context.dot_property(idIndex + 1) != null)
                Rec(ref path, idIndex + 1, astVisitor.Database.GetEntityType(type)!, out type);
        }

        public void ParseProperty(ref PropertyPath path, MoiraiParser.Property_idContext rootProp, EntityType owningType, out PropertyValue.ValueType type)
        {
            string propertyName = rootProp.GetText();
            var propertyId = owningType.GetPropertyId(propertyName);
            if (!propertyId.IsValid)
            {
                type = default;
                astVisitor.AddError(ErrorCode.UnknownProperty, rootProp, propertyName);
                return;
            }

            type = owningType.GetPropertyType(propertyName);
            astVisitor.Linker?.LinkProperty(rootProp, propertyId);
            path.AddProperty(propertyId);
        }
    }
}
