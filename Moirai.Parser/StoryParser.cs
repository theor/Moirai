using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection.Metadata;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace Moirai.Parser;


[Generators.EnumFromConstants("Rules","RULE_")]
public partial class MoiraiParser
{
    // static void Test()
    // {
    //     var x = MoiraiParser.Rules.If;
    // }
}

[Generators.EnumFromConstants("Tokens","")]
public partial class moirai_lexer
{
    static void Test()
    {
    var x = MoiraiParser.Rules.If;
    // var y = moirai_lexer.Tokens.IF;
    }
}

public class FunctionDescriptor : IFunctionDescriptor
{
    public record ParseContext(StoryParser.AstVisitor Visitor, ParserRuleContext CallContext)
    {
        public int ParseVariable(out EntityTypeId entityTypeId, out PropertyValue.ValueType type)
        {
            EntityType varType = ParseEntityType();

            int varIndex;
            if (CallContext is MoiraiParser.CallContext c)
            {
                Visitor.DeclareVar(c.VAR_ID().GetText(), varType.RefType, c.VAR_ID().Symbol, out varIndex);
            }
            else
            {
                var rawCallContext = (MoiraiParser.Raw_callContext) CallContext;
                Visitor.DeclareVar(rawCallContext.VAR_ID().GetText(), varType.RefType, rawCallContext.VAR_ID().Symbol,
                    out varIndex);
            }

            entityTypeId = varType.Id;
            type = varType.RefType;
            return varIndex;
        }

        public ParserRuleContext GetArgumentToken(int index)
        {
            if (CallContext is MoiraiParser.CallContext c)
            {
                return c.expr(index);
            }
            else if (CallContext is MoiraiParser.Raw_callContext r)
            {
                return r.value();
            }

            return CallContext;
        }

        public IValue ParseArgument(int index, out PropertyValue.ValueType type)
        {
            if (CallContext is MoiraiParser.CallContext c)
            {
                return Visitor.ParseExpr(c.expr(index), out type)!;
            }
            else if (CallContext is MoiraiParser.Raw_callContext r)
            {
                if (index != 0)
                {
                    Visitor.AddError(StoryParser.ErrorCode.MissingArgument, CallContext,
                        "Expected more arguments, convert to () syntax");
                    type = default;
                    return default!;
                }

                return Visitor.ParseValue(r.value(), out type);
            }

            type = default;
            return default!;
        }
        public IValue ParseArgument(int index)
        {
            return ParseArgument(index, out _);
        }

        public int ArgCount => CallContext is MoiraiParser.CallContext c
            ? c.expr().Length
            : (CallContext is MoiraiParser.Raw_callContext r && r.value() != null ? 1 : 0);

        public EntityType ParseEntityType()
        {
            ITerminalNode t;
            if (CallContext is MoiraiParser.CallContext c)
                t = StoryParser.GetTypeTerminal(c.type());
            else
                t = StoryParser.GetTypeTerminal(((MoiraiParser.Raw_callContext) CallContext).type());
            EntityTypeId type = Visitor.Database.GetEntityType(Visitor.ParseType(t))?.Id ?? EntityTypeId.Null;
            
            Visitor.Linker?.LinkType(new StoryParser.AstVisitor.FileRange(t.Symbol), type);
            if (type == EntityTypeId.Null)
            {
                Visitor.AddError(StoryParser.ErrorCode.UnknownEntityType, GetArgumentToken(0), $"'{type}'");
            }

            return this.Visitor.Database.Types[(int) type.Id];
        }

        public string GetText(RuleContext expr) => Visitor.Parser.TokenStream.GetText(expr);

        public MoiraiParser.ScopeContext GetScopeContext()
        {
            MoiraiParser.ScopeContext scopeContext = CallContext is MoiraiParser.CallContext c
                ? c.scope()
                : ((MoiraiParser.Raw_callContext)CallContext).scope();
            return scopeContext;
        }

        public void ExpectArgcount(int i, bool isMaxCount = false)
        {
            if (isMaxCount ? ArgCount > i : ArgCount != i)
                Visitor.AddError(StoryParser.ErrorCode.MissingArgument, CallContext,
                    $"Expected {i} arguments{(isMaxCount ? " max" : "")}, got {ArgCount}");
        }

        public IValue ParsePredicate(EntityTypeId entityTypeId)
        {
            if (ArgCount == 1)
                return ParseArgument(0);
            IValue[] preds = new IValue[ArgCount];
            for (int i = 0; i < ArgCount; i++)
            {
                preds[i] = ParseArgument(i);
            }

            return new And(preds);
        }
    }

    public delegate (IValueCall, PropertyValue.ValueType) ParseCallDelegate(ParseContext context);

    public string FuncName { get; }
    public bool ExpectVariable { get; }
    public string? Documentation { get; }
    private readonly ParseCallDelegate _parse;

    public FunctionDescriptor(string funcName, bool expectVariable, ParseCallDelegate parse, string? documentation = null)
    {
        FuncName = funcName;
        ExpectVariable = expectVariable;
        Documentation = documentation;
        _parse = parse;
    }

    public IValueCall Parse(StoryParser.AstVisitor parser, MoiraiParser.Raw_callContext call,
        out PropertyValue.ValueType returnType)
    {
        (IValueCall, PropertyValue.ValueType) c = _parse(new ParseContext(parser, call));
        returnType = c.Item2;
        if (c.Item1 != null)
            c.Item1.FunctionDescriptor = this;
        else
            throw new InvalidOperationException(parser.Parser.TokenStream.GetText(call));
        return c.Item1;
    }

    public IValueCall Parse(StoryParser.AstVisitor parser, MoiraiParser.CallContext call,
        out PropertyValue.ValueType returnType)
    {
        (IValueCall, PropertyValue.ValueType) c = _parse(new ParseContext(parser, call));
        returnType = c.Item2;
        if (c.Item1 != null)
            c.Item1.FunctionDescriptor = this;
        else
            parser.AddError(StoryParser.ErrorCode.UnknownFunction, call, "");
        return c.Item1;
    }


    public string Print(StoryPrinter printer, IValueCall call)
    {
        // call (1,2)
        // call X $x: (12)
        // call X $x
        var args = call.GetArgs(printer);
        switch ((call.VariableIndex.HasValue, args.Count()))
        {
            case (false, 0):
                return ("not a call??");
            case (false, _):
                return $"{FuncName} ({string.Join(", ", call.GetArgs(printer).Select(a => printer.Print(a)))})";
            case (true, 0):
                return $"{FuncName} {printer.Print(call.VariableIndex!.Value.Item2)} ${call.VariableIndex.Value.Item1}";
            case (true, _):
                return
                    $"{FuncName} {printer.Print(call.VariableIndex!.Value.Item2)} ${call.VariableIndex.Value.Item1}: ({string.Join(", ", call.GetArgs(printer).Select(a => printer.Print(a)))})";
        }
    }
}

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
                using var vs = new AstVisitor.VariableDeclarationScope(ctx.Visitor, scopeContext);
                var variableIndex = ctx.ParseVariable(out var etid, out _);
                return (new AssignPick(etid, variableIndex, ctx.ParsePredicate(etid),
                        CallType.Each, ctx.Visitor.ParseRawScope(scopeContext, out _)),
                    PropertyValue.TypeTypedRef(etid));
            }),
        new("pick", true,
            ctx =>
            {
                var variableIndex = ctx.ParseVariable(out var etid, out _);
                return (new AssignPick(etid, variableIndex, ctx.ParsePredicate(etid),
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
        void DeclareType(AstVisitor.FileRange range, EntityTypeId typeId, string? lineDefinition = null);
        void DeclareTypeProperty(AstVisitor.FileRange range, PropertyId propertyDefinitionPropertyId, string? lineDefinition = null);
        void LinkType(AstVisitor.FileRange range, EntityTypeId entityType);
        void LinkProperty(AstVisitor.FileRange range, PropertyId propertyId);
        void DeclareEnum(AstVisitor.FileRange range, EnumDefinitionId enumId);
        void LinkEnum(StoryParser.AstVisitor.FileRange range, EnumDefinitionId enumId);
        void LinkEnumMember(AstVisitor.FileRange range, PropertyValue enumValue);
        void LinkVariable(AstVisitor.FileRange varId, AstVisitor.VariableDeclaration decl);
        void DeclareVariable(AstVisitor.FileRange range, AstVisitor.VariableDeclaration variableDeclaration,
            AstVisitor.FileRange variableScope);
        void DeclareFunction(AstVisitor.FileRange fileRange, FunctionDescriptor descriptor, string? inlineDef = null);
        void LinkFunction(AstVisitor.FileRange range, FunctionDescriptor descriptor);
    }
    public class AstVisitor : MoiraiParserBaseVisitor<object?>, IVisitor
    {
        public record struct VariableDeclaration(string Name, PropertyValue.ValueType Type, FileRange DeclarationRange)
        {
        }

        public record struct FilePosition(int Line, int Column) : IComparable<FilePosition>
        {
            public int CompareTo(FilePosition other)
            {
                var lineComparison = Line.CompareTo(other.Line);
                if (lineComparison != 0) return lineComparison;
                return Column.CompareTo(other.Column);
            }

            public static bool operator <(FilePosition left, FilePosition right)
            {
                return left.CompareTo(right) < 0;
            }

            public static bool operator >(FilePosition left, FilePosition right)
            {
                return left.CompareTo(right) > 0;
            }

            public static bool operator <=(FilePosition left, FilePosition right)
            {
                return left.CompareTo(right) <= 0;
            }

            public static bool operator >=(FilePosition left, FilePosition right)
            {
                return left.CompareTo(right) >= 0;
            }
        }

        public record FileRange(FilePosition Start, FilePosition End)
        {
            public static readonly FileRange Empty = new FileRange(new FilePosition(-1,-1), new FilePosition(-1,-1));
            public static implicit operator FileRange(ParserRuleContext rule) => new(rule);
            // public static implicit operator FileRange(ITerminalNode token) => new(token.Symbol);

            public FileRange(ParserRuleContext symbol) : this(
                new FilePosition(symbol.Start.Line - 1, symbol.Start.Column),
                GetEnd(symbol)
                )
            {
            }

            private static FilePosition GetEnd(ParserRuleContext symbol)
            {
                if (symbol.Stop == null || symbol.Stop == symbol.Start)
                    return new(symbol.Start.Line - 1, symbol.Start.Column + symbol.GetText().Length);
                return new FilePosition(symbol.Stop.Line - 1, symbol.Stop.Column);
            }

            public FileRange(IToken symbol) : this(new FilePosition(symbol.Line - 1, symbol.Column),
                new FilePosition(symbol.Line - 1, symbol.Column + symbol.Text.Length))
            {
            }
            public FileRange(ITerminalNode symbol) : this(symbol.Symbol)
            {
            }

            public bool Contains(FilePosition pos)
            {
                return Start.Line <= pos.Line && pos.Line <= End.Line &&
                       (Start.Line != pos.Line || Start.Column <= pos.Column) &&
                       (End.Line != pos.Line || End.Column >= pos.Column);
            }
        }

        public class VariableScope(VariableScope? parent, FileRange range)
        {
            public readonly int ParentCount = parent == null ? 0 : parent.ParentCount + parent.Variables.Count;
            public readonly FileRange Range = range;
            public readonly VariableScope? Parent = parent;
            public readonly List<VariableScope> Children = new();
            public readonly List<VariableDeclaration> Variables = new();
            public int Count => ParentCount + Variables.Count;
            public VariableDeclaration this[int index] => index < ParentCount ? Parent![index] : Variables[index - ParentCount];

            public bool GetDeclarationAndRange(int index, out VariableDeclaration decl, out FileRange range)
            {
                if (index == -1)
                {
                    decl = default;
                    range = null;
                    return false;
                }

                if (index < ParentCount)
                    return Parent!.GetDeclarationAndRange(index, out decl, out range);
                decl = Variables[index - ParentCount];
                range = Range;
                return true;
            }

            public int GetVariableIndexByName(string name, out VariableDeclaration decl)
            {
                var findLastIndex = Variables.FindLastIndex(v => v.Name == name);
                if (findLastIndex == -1)
                {
                    if (Parent != null)
                        return Parent.GetVariableIndexByName(name, out decl);
                    decl = default;
                    return -1;
                }

                decl = Variables[findLastIndex];
                return findLastIndex + ParentCount;
            }
        }

        public (int offsetLine, int offsetColumn) Offset { get; set; }

        public readonly VariableScope RootScope;
        private VariableScope _current;
        
        public List<Error> Errors { get; } = new();

        public MoiraiParser Parser { get; set; }
        public ILinker? Linker { get; set; }
        protected override object? DefaultResult => null;

        public readonly Database Database;

        public AstVisitor(Database database, MoiraiParser parser)
        {
            Database = database;
            Parser = parser;
            RootScope = new(null, FileRange.Empty);
            _current = RootScope;
        }

        public override object? VisitR(MoiraiParser.RContext context)
        {
            foreach (var enumDefinitionContext in context.enum_definition())
            {
                enumDefinitionContext.Accept(this);
            }

            List<(EntityType Id, MoiraiParser.Type_definitionContext attr)> typesContexts = new();
            List<(EntityType Id, MoiraiParser.AttributeContext attr)> deferredTypeAttributes = new();

            foreach (var typeDefinitionContext in context.type_definition())
            {
                if (typeDefinitionContext.TYPE_ID() == null)
                    return AddError(ErrorCode.TypenameMustStartWithUpperCase,
                        typeDefinitionContext,
                        typeDefinitionContext.GetText());

                string? typeName = typeDefinitionContext.TYPE_ID().GetText();
                EntityType type = DeclareEntityType(typeName);

                Linker?.DeclareType(new FileRange(typeDefinitionContext), type.Id);

                typesContexts.Add((type, typeDefinitionContext));
                foreach (var attr in typeDefinitionContext.attribute())
                    deferredTypeAttributes.Add((type, attr));
            }

            foreach (var (type, typeDefinitionContext) in typesContexts)
            {
                foreach (MoiraiParser.Prop_definitionContext? propDefinitionContext in typeDefinitionContext.prop_definition())
                {
                    var propName = propDefinitionContext.property_id().GetText();
                    if (type.GetPropertyId(propName).Id != 0)
                        return AddError(ErrorCode.DuplicatePropertyDefinition, typeDefinitionContext, propName);

                    PropertyValue.ValueType proptype =
                        ParseType(GetTypeTerminal(propDefinitionContext.type()));
                    var propertyDefinition = new PropertyDefinition(propName, type.Id, (uint) type.Properties.Count,
                        proptype);
                    type.Properties.Add(propertyDefinition);
                    Linker?.DeclareTypeProperty(new FileRange(propDefinitionContext), propertyDefinition.PropertyId);
                }

                foreach (var functionDefinitionContext in typeDefinitionContext.function_definition())
                {
                    ParseFunctionDefinition(functionDefinitionContext, type);
                }
            }

            foreach (var (tid, attr) in deferredTypeAttributes)
            {
                var id = attr.ID();
                if (id.GetText() != "display")
                {
                    AddError(ErrorCode.UnknownAttribute, id, id.GetText() ?? "??");
                    continue;
                }

                if (attr.expr().Length < 2)
                {
                    AddError(ErrorCode.MissingArgument, attr,
                        "display expects two arguments, a string and and expression");
                    continue;
                }

                var refReferencedType = ParseType(attr.type_id().TYPE_ID());
                if (!refReferencedType.IsRefType)
                    AddError(ErrorCode.UnknownEntityType, attr, "expected an Entity type");

                using (new VariableDeclarationScope(this, attr))
                {
                    DeclareVar("$self", tid.RefType, id.Symbol, out var varIndex);
                    DeclareVar("$other", refReferencedType, id.Symbol, out var otherVarIndex);
                    var expr = ParseExpr(attr.expr(1))!;
                    InterpolatedString? itemDisplay = null;
                    if (attr.expr(2)?.value()?.@string() != null)
                        itemDisplay = ParseInterpolatedString(attr.expr(2).value().@string());
                    Display d = new Display(Database.GetEntityType(refReferencedType), varIndex, otherVarIndex,
                        attr.expr(0).GetText(), expr, itemDisplay);
                    var t = Database.Types[(int) (tid.Id.Id)];

                    t.Attributes.Add(d);
                }
            }

            foreach (var fundef in context.function_definition())
            {
                ParseFunctionDefinition(fundef);
            }

            foreach (var child in context.children)
            {
                if (child is MoiraiParser.EventContext e)
                    e.Accept(this);
                else if (child is MoiraiParser.TriggerContext t)
                    t.Accept(this);
            }

            return null;
        }

        private void ParseFunctionDefinition(MoiraiParser.Function_definitionContext fundef, EntityType? instanceType = null)
        {
            using var _ = new VariableDeclarationScope(this, fundef.scope());

            var name = fundef.fun_id().GetText();
            PropertyValue.ValueType returnType = PropertyValue.ValueType.Null;
            if(fundef.type() != null)
            {
                returnType = ParseType(GetTypeTerminal(fundef.type()));
            }

            if(instanceType != null)
                DeclareVar("$self", instanceType.RefType, fundef.FUNCTION().Symbol, out var varIndex);
            
            var parameters = fundef.param().Select(p =>
            {
                var paramName = p.VAR_ID().GetText();
                var paramType = ParseType(GetTypeTerminal(p.type()));
                DeclareVar(paramName, paramType, p.VAR_ID().Symbol, out var paramIndex);
                return new FunctionDefinition.Parameter(paramName, paramType, paramIndex);
            }).ToArray();
            var functionDefinitionId = new FunctionDefinitionId(
                (ushort)(instanceType == null ? Database.Functions.Count : instanceType.Functions.Count));
            var functionDefinition = new FunctionDefinition(functionDefinitionId,
                name,
                instanceType?.Id ?? EntityTypeId.Null,
                returnType,
                parameters,
                ParseScope(fundef.scope(), out var actualType));
            if(instanceType == null)
                this.Database.Functions.Add(functionDefinition);
            else
                instanceType.Functions.Add(functionDefinition);
            if(actualType != returnType)
                AddError(actualType == PropertyValue.ValueType.Null ? ErrorCode.MissingReturnValue : ErrorCode.MismatchedReturnType, fundef, $"{actualType} != {returnType}");
        }


        public override object? VisitType_definition(MoiraiParser.Type_definitionContext context)
        {
            throw new NotImplementedException();
        }

        public EntityType DeclareEntityType(string typeName)
        {
            var id = (uint) Database.Types.Count;
            var entityType = new EntityType(typeName, id);
            Database.Types.Add(entityType);
            return entityType;
        }

        public override object? VisitProp_definition(MoiraiParser.Prop_definitionContext context)
        {
            throw new NotImplementedException();
        }

        public PropertyValue.ValueType ParseType(ITerminalNode id)
        {
            switch (id.GetText())
            {
                case "bool": return PropertyValue.TypeBool;
                // case "ref": return PropertyValue.TypeRef;
                case "number": return PropertyValue.TypeNumber;
                case "float": return PropertyValue.TypeFloat;
                case "string": return PropertyValue.TypeString;
                case "percentage": return PropertyValue.TypePercent;
                default:
                    if (Database.GetEnumDefinition(id.GetText(), out EnumDefinition enumDefinition))
                    {
                        Linker?.LinkEnum(new(id), enumDefinition.Index);
                        return PropertyValue.TypeEnum(enumDefinition.Index);
                    }
                    var entityType = Database.GetEntityType(id.GetText());
                    Linker?.LinkType(new(id), entityType.Id);
                    if (entityType.Id.IsValid)
                        return entityType.RefType;
                    AddError(ErrorCode.UnknownPropertyType, id, id.GetText());
                    return default;
            }
        }

        public override object? VisitEnum_definition(MoiraiParser.Enum_definitionContext context)
        {
            EnumDefinition en = new(new EnumDefinitionId((ushort) Database.Enums.Count), context.TYPE_ID(0).GetText(),
                context.TYPE_ID().Skip(1).Select(v => v.GetText()).ToList());
            Database.Enums.Add(en);
            Linker?.DeclareEnum(context, en.Index);
            return null;
        }

        public override object? VisitEvent(MoiraiParser.EventContext context)
        {
            string actionId = context.ID().GetText();
            using var _ = new VariableDeclarationScope(this, context.scope());
            IFilter? f = null;
            if (context.filter() != null)
            {
                var p = context.filter();
                var args = p.expr();
                switch (p.attr.Text)
                {
                    case "start":
                        f = new FilterAtStart();
                        break;
                    case "frequency":
                        if (args.Length != 3)
                        {
                            AddError(ErrorCode.MissingArgument, context.filter(), "frequency expects 3 arguments");
                        }

                        if (!ParseEnum<Database.Frequency>(args[1].value(), out var val))
                            // .TryParse<Database.Frequency>(args[1].GetText(), out var freq))
                            AddError(ErrorCode.UnknownEnum, args[1],
                                "Should be a value among " + string.Join(", ", Enum.GetNames<Database.Frequency>()));
                        var x = int.Parse(args[0].GetText());
                        var y = int.Parse(args[2].GetText());
                        switch (val)
                        {
                            case Database.Frequency.EveryXYear:
                                f = new FilterExactlyXEveryYYears(x, y, Database.Actions.Count + 1);
                                break;
                            case Database.Frequency.PerXYear:
                                f = new FilterProbabilityXPerYears(x, y);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        break;
                    default:
                        AddError(ErrorCode.UnknownCall, context.filter(), "Unknown attribute");
                        break;
                    // case "every":
                    // {
                    //     var x = int.Parse(p.occurence.Text);
                    //     var y = int.Parse(p.years.Text);
                    //     f = new FilterExactlyXEveryYYears(x, y, Database.Actions.Count + 1);
                    //     break;
                    // }
                    // case "per":
                    // {
                    //     var x = int.Parse(p.occurence.Text);
                    //     var y = int.Parse(p.years.Text);
                    //     f = new FilterProbabilityXPerYears(x, y);
                    //     break;
                    // }
                }
            }


            var cats = ParseCategories(context.categories());

            CurrentEventTrigger = new EventTrigger(Database.Actions.Count + 1, actionId, false, f, cats);
            foreach (MoiraiParser.EffectContext effectContext in context.scope().effect())
            {
                // if (effectContext.comment() != null)
                //     continue;
                var effect = ParseEffect(effectContext, out var _);
                if (effect == null)
                {
                    AddError(ErrorCode.NullEffect, effectContext, effectContext.GetText());
                    continue;
                }

                CurrentEventTrigger.Effects.Add(effect);
            }

            Database.Actions.Add(CurrentEventTrigger);
            CurrentEventTrigger = null;
            return null;
        }

        private CategoryId[] ParseCategories(MoiraiParser.CategoriesContext tagIds)
        {
            CategoryId[] tags = new CategoryId[tagIds.ID().Length];
            var nodes = tagIds;
            for (var index = 0; index < nodes.ID().Length; index++)
            {
                var cat = tagIds.ID(index);
                tags[index] = Database.GetCategoryId(cat.GetText());
            }

            return tags;
        }


        public EventTrigger? CurrentEventTrigger;

        public override object? VisitTrigger(MoiraiParser.TriggerContext context)
        {
            string actionId = context.ID().GetText();
            var categories = ParseCategories(context.categories());
            CurrentEventTrigger = new EventTrigger(Database.Triggers.Count + 1, actionId, true, null, categories);

            using var _ = new VariableDeclarationScope(this, context.scope());
            if (context.scope().when_created() is { } createdContext)
            {
                EntityType type = Database.GetEntityType(createdContext.type_id().TYPE_ID().GetText());
                if (!type.Id.IsValid)
                    AddError(ErrorCode.UnknownPropertyType, createdContext,
                        createdContext.type_id().TYPE_ID()?.GetText() ?? createdContext.GetText());

                DeclareVar("$new", type.RefType, createdContext.WHEN_CREATED().Symbol, out var _);
                CurrentEventTrigger.When = (EventTrigger.WhenType.Created, type.Id,
                    ParsePredicate(createdContext.expr()));
            }
            else if (context.scope().when() is { } whenContext)
            {
                EntityType type = Database.GetEntityType(whenContext.type_id().TYPE_ID().GetText());
                if (!type.Id.IsValid)
                    AddError(ErrorCode.UnknownPropertyType, whenContext, whenContext.type_id().TYPE_ID().GetText());

                DeclareVar("$old", type.RefType, whenContext.WHEN().Symbol, out var _);
                DeclareVar("$new", type.RefType, whenContext.WHEN().Symbol, out var _);
                CurrentEventTrigger.When = (EventTrigger.WhenType.Changed, type.Id, ParsePredicate(whenContext.expr()));
            }

            Database.Triggers.Add(CurrentEventTrigger);
            foreach (var effectContext in context.scope().effect())
            {
                // if (effectContext.comment() != null)
                // continue;
                var effect = ParseEffect(effectContext, out var _);
                if (effect != null)
                    CurrentEventTrigger.Effects.Add(effect);
            }

            CurrentEventTrigger = null;
            return null;
        }

        private IInstruction ParseEffect(MoiraiParser.EffectContext effectContext, out PropertyValue.ValueType type)
        {
            if (effectContext.expr() != null)
            {
                var value = ParseExpr(effectContext.expr(), out type);
                if (value != null)
                    return new CallInstruction(value);
            }

            type = PropertyValue.ValueType.Null;
            if (effectContext.var() != null)
                return ParseLocalVar(effectContext.var());
            if (effectContext.set() != null)
                return ParseSet(effectContext.set());


            AddError(ErrorCode.Exception, effectContext, "NULL");
            return new SetProperty(default, null, false);
        }

        private bool _parsingMatchCase;

        private IValue ParseMatch(MoiraiParser.MatchContext match, out PropertyValue.ValueType valueType)
        {
            bool weight = match.MATCH_WEIGHT() != null;
            var values = match.expr().Select(ParseExpr).ToArray();
            (int, IInstruction[])[] weights = default;
            (IValue?[], IInstruction[])[] cases = default;
            if (weight)
            {
                if (values.Length > 1)
                    AddError(ErrorCode.WeightMatchTakesOnlyOneValue, match.expr(1), values.Length.ToString());
                weights = new (int, IInstruction[])[match.match_case().Length];
            }
            else
            {
                cases = new (IValue?[], IInstruction[])[match.match_case().Length];
            }

            int accWeight = 0;
            valueType = PropertyValue.ValueType.Null;
            for (int i = 0; i < match.match_case().Length; i++)
            {
                var caseCtx = match.match_case(i);
                _parsingMatchCase = true;
                IValue[] caseValues;
                try
                {
                    // TODO type ?
                    caseValues = caseCtx.value().Select(x => ParseValue(x, out var _)).ToArray();
                }
                finally
                {
                    _parsingMatchCase = false;
                }

                using var _ = new VariableDeclarationScope(this, (ParserRuleContext)caseCtx.scope() ?? caseCtx.effect());
                var instrs = caseCtx.scope() == null
                    ? new[] {ParseEffect(caseCtx.effect(), out valueType)}
                    : ParseRawScope(caseCtx.scope(), out valueType);
                if (weight)
                {
                    int w;
                    if (caseValues[0] is MatchAnyValue)
                    {
                        if (i != match.match_case().Length - 1)
                            AddError(ErrorCode.MatchAnyValueMustBeLast, caseCtx.value(0), caseCtx.value(0).GetText());
                        weights[i] = (-1, instrs);
                    }
                    else
                    {
                        w = ((Literal) caseValues[0]).Value.IntValue;
                        if (w <= 0)
                            AddError(ErrorCode.MatchNullWeight, caseCtx.value(0), caseCtx.value(0).GetText());
                        accWeight += w;
                        weights[i] = (accWeight, instrs);
                    }
                }
                else
                    cases[i] = (caseValues, instrs);
            }

            if (weight)
                return new MatchWeight(values[0], weights);

            return new Match(values, cases);
        }

        private If ParseIf(MoiraiParser.IfContext @if, out PropertyValue.ValueType valueType)
        {
            var elseType = PropertyValue.ValueType.Null;
            var iff = new If(ParseExpr(@if.cond), ParseScope(@if.then, out var ifType),
                @if.@else == null ? Array.Empty<IInstruction>() : ParseScope(@if.@else, out elseType));
            valueType = @if.@else == null ? ifType : Cast(ifType, elseType);
            return iff;
        }

        public IValue ParsePredicate(MoiraiParser.ExprContext[] exprContexts)
        {
            var exprs = exprContexts;
            if (exprContexts.Length == 0) return null;

            var predicate = exprs.Length == 1
                ? ParseExpr(exprs[0])!
                : new And(exprs.Select(x => ParseExpr(x)).Where(e => e != null).Cast<IValue>().ToList());
            return predicate;
        }

        public override object? VisitWhen(MoiraiParser.WhenContext context)
        {
            throw new NotImplementedException();
        }

        public override object? VisitSet(MoiraiParser.SetContext context)
        {
            throw new NotImplementedException();

            return null;
        }

        private SetProperty ParseLocalVar(MoiraiParser.VarContext context)
        {
            var name = context.VAR_ID();
            var expr = ParseExpr(context.expr(), out var type);
            DeclareVar(name.GetText(), type, name.Symbol, out var varIndex);
            return new SetProperty(new PropertyPath(varIndex), expr, true);
        }

        private SetProperty ParseSet(MoiraiParser.SetContext context)
        {
            var left = ParsePath(context.path(), out var assignedType);
            var right = ParseExpr(context.expr(), out var rightType); //, left.Property);
            if (assignedType != Cast(assignedType, rightType))
                AddError(ErrorCode.MismatchedAssignmentTypes, context, $"{assignedType} != {rightType}");
            return new SetProperty(left, right, false);
        }

        static PropertyValue.ValueType Cast(PropertyValue.ValueType to, PropertyValue.ValueType from)
        {
            if (to == from)
                return to;

            if (to == PropertyValue.TypeFloat)
            {
                if (from == PropertyValue.TypeNumber || from == PropertyValue.TypePercent)
                    return PropertyValue.TypeFloat;
            }

            if (to == PropertyValue.TypePercent)
            {
                if (from == PropertyValue.TypeNumber || from == PropertyValue.TypeFloat)
                    return PropertyValue.TypePercent;
            }

            if (to.BaseType == PropertyValue.ValueBaseType.Enum && from == PropertyValue.TypeNumber)
                return to;
            if (from.BaseType == PropertyValue.ValueBaseType.Enum && to == PropertyValue.TypeNumber)
                return to;
            if (to.IsRefType && from.IsRefType && from.Index == 0) // null or (shaky) untyped ref
                return to;

            return from;
        }

        public IValue ParseValue(MoiraiParser.ValueContext value, out PropertyValue.ValueType type)
        {
            if (_parsingMatchCase && value.path()?.GetText() == "_")
            {
                // TODO ?
                type = default;
                return MatchAnyValue.Instance;
            }

            if (value.type_id()?.TYPE_ID() != null)
            {
                var etype = Database.GetEntityType(value.type_id().TYPE_ID().GetText());
                if (!etype.Id.IsValid)
                {
                    if (Database.GetEnumDefinition(value.type_id().TYPE_ID().GetText(), out var ed))
                    {
                        // TODO really ?
                        type = ed.ValueType;
                        return new Literal(ed.EnumType);
                    }

                    AddError(ErrorCode.UnknownPropertyType, value, value.type_id().TYPE_ID().GetText());
                }

                type = etype.RefType;
                return new Literal(etype.Id);
            }

            if (value.call() != null)
            {
                return ParseCall(value.call(), out type);
            }

            if (value.raw_call() != null)
            {
                return ParseRawCall(value.raw_call(), out type);
            }

            if (value.path() != null)
            {
                var path = ParsePath(value.path(), out type);
                return path;
            }

            if (value.@string() != null)
            {
                type = PropertyValue.TypeString;
                return ParseInterpolatedString(value.@string());
            }

            if (value.NULL() != null)
            {
                type = PropertyValue.TypeRef;
                return new Literal(EntityId.Null);
            }

            if (value.number() is { } number)
            {
                if (number.NUMBER_FLOAT() != null)
                {
                    type = PropertyValue.TypeFloat;
                    return new Literal(float.Parse(number.NUMBER_FLOAT().GetText()));
                }

                if (number.PERCENT() != null)
                {
                    type = PropertyValue.TypePercent;
                    return new Literal(PropertyValue.Percent(int.Parse(number.PERCENT().GetText()
                        .Substring(0, number.PERCENT().GetText().Length - 1))));
                }

                type = PropertyValue.TypeNumber;
                return new Literal(int.Parse(number.GetText()));
            }

            if (value.@bool() != null)
            {
                type = PropertyValue.TypeBool;
                return new Literal(value.@bool().TRUE() != null);
            }

            var enumValueContext = value.enum_value();
            if (ParseEnum(out type, enumValueContext, out var addError)) return addError;

            throw new ArgumentOutOfRangeException();
        }

        private bool ParseEnum<T>(MoiraiParser.ValueContext valueContext, out T val) where T : struct, Enum
        {
            ITerminalNode enumValue;

            if (valueContext.enum_value() != null)
            {
                var enumType = valueContext.enum_value().TYPE_ID(0);
                if (enumType.GetText() != typeof(T).Name)
                {
                    val = default;
                    AddError(ErrorCode.MismatchedAssignmentTypes, enumType,
                        "Expected an enum of type " + typeof(T).Name);
                    return false;
                }
                enumValue = valueContext.enum_value().TYPE_ID(1);
            }
            else
                enumValue = valueContext.type_id().TYPE_ID();

            return Enum.TryParse(enumValue.GetText(), out val);
        }

        private bool ParseEnum(out PropertyValue.ValueType type, MoiraiParser.Enum_valueContext? enumValueContext,
            out IValue addError)
        {
            if (enumValueContext != null)
            {
                var enumType = enumValueContext.TYPE_ID(0);
                if (!Database.GetEnumDefinition(enumType.GetText(), out var enumDef))
                {
                    type = default;
                    {
                        addError = (AddError(ErrorCode.UnknownEnum, enumType, enumType.GetText()) as IValue)!;
                        return true;
                    }
                }
                Linker?.LinkEnum(new(enumType), enumDef.Index);

                var enumValue = enumValueContext.TYPE_ID(1);
                if (!enumDef.GetValueFromName(enumValue.GetText(), out var val))
                {
                    type = default;
                    {
                        addError = (AddError(ErrorCode.UnknownEnumValue, enumValue,
                            enumValue.GetText() + " in enum " + enumDef.Name) as IValue)!;
                        return true;
                    }
                }

                Linker?.LinkEnumMember(new FileRange(enumValue), val);
                type = enumDef.ValueType;
                {
                    addError = new Literal(val);
                    return true;
                }
            }

            type = default;
            addError = default!;
            return false;
        }

        public VariableDeclaration DeclareVar(string variable, PropertyValue.ValueType type, IToken contextStart, out int varIndex)
        {
            // if ((varIndex = GetVariableIndexByName(variable)) != -1)
            // {
            //     // AddError(ErrorCode.DuplicateVariableDefinition,  contextStart, " Duplicate variable " + variable);
            //     // varIndex = 0;
            //     return true;
            // }

            var variableDeclaration = new VariableDeclaration(variable, type, new FileRange(contextStart));
            _current.Variables.Add(variableDeclaration);
            // Linker?.DeclareVariable(_current.Range, variableDeclaration);
            Linker?.DeclareVariable(variableDeclaration.DeclarationRange, variableDeclaration, variableScope: _current.Range);
            varIndex = _current.Count - 1;
            return variableDeclaration;
        }

        public struct VariableDeclarationScope : IDisposable
        {
            private readonly AstVisitor _astVisitor;
            private readonly ParserRuleContext _scope;

            public VariableDeclarationScope(AstVisitor astVisitor, ParserRuleContext scope)
            {
                if (scope == null)
                    return;
                _astVisitor = astVisitor;
                _astVisitor.PushScope(scope);
                _scope = scope;
            }

            public void Dispose()
            {
                _astVisitor?.PopScope();
            }
        }

        private void PushScope(ParserRuleContext scope)
        {
            VariableScope newScope = new(_current, new FileRange(scope));
            _current.Children.Add(newScope);
            _current = newScope;
        }
        private void PopScope()
        {
            if(_current.Parent == null)
                throw new InvalidOperationException("Null parent scope");
            _current = _current.Parent!;
        }

        private IValue ParseRawCall(MoiraiParser.Raw_callContext context, out PropertyValue.ValueType returnType)
        {
            var funcName = context.fun_id().GetText();
            if(Database.GetFunctionDefinition(funcName, out var fd))
            {
                var ctx = new FunctionDescriptor.ParseContext(this, context);
                return ParseUserFunctionCall(this, fd.Value, ctx, out returnType);
            }
            if(GetFunctionDescriptor(funcName, out var f));
            {
                return f.Parse(this, context, out returnType);
            }

            returnType = default!;
            return (AddError(ErrorCode.UnknownInstruction, context, funcName) as IValue)!;
        }


        private IValue ParseCall(MoiraiParser.CallContext context, out PropertyValue.ValueType returnType)
        {
            var funcName = context.fun_id().GetText();
            if(Database.GetFunctionDefinition(funcName, out var fd))
            {

                var ctx = new FunctionDescriptor.ParseContext(this, context);
                return ParseUserFunctionCall(this, fd.Value, ctx, out returnType);
            }
            if(GetFunctionDescriptor(funcName, out var f))
            {
                Linker?.LinkFunction(new FileRange(context.fun_id()), f);
                return f.Parse(this, context, out returnType);
            }

            returnType = default!;
            return (AddError(ErrorCode.UnknownInstruction, context, funcName) as IValue)!;
        }

        private UserFunctionCall ParseUserFunctionCall(AstVisitor astVisitor, FunctionDefinition definition,
            FunctionDescriptor.ParseContext ctx, out PropertyValue.ValueType returnType)
        {
            UserFunctionCall call = new(definition, 
                // TODO check arg/param type
                definition.Parameters.Skip(definition.IsInstanceMethod ? 1 : 0).Select((p,i) =>
                {
                   
                    var argument =   ctx.ParseArgument(i, out var type);
                    if(argument == null)
                        AddError(ErrorCode.MissingArgument, ctx.CallContext, $"Missing argument {i}: {p.ParamName}: {astVisitor.Database.Printer.Print(p.ParamType)}");
                    else if (type != p.ParamType)
                        AddError(ErrorCode.MismatchedAssignmentTypes, ctx.GetArgumentToken(i), $"Expected {astVisitor.Database.Printer.Print(p.ParamType)} got {astVisitor.Database.Printer.Print(type)}");
                    return argument;
                }).ToArray()
                );

            returnType = definition.ReturnType;
            return call;
        }

        public IInstruction[] ParseScope(MoiraiParser.ScopeContext? scopeContext, 
            out PropertyValue.ValueType type)
        {
            // TODO 
            type = PropertyValue.ValueType.Null;
            if (scopeContext == null)
                return Array.Empty<IInstruction>();
            using var vs = new VariableDeclarationScope(this, scopeContext);

            return ParseRawScope(scopeContext, out type);
        }

        public IInstruction[] ParseRawScope(MoiraiParser.ScopeContext scopeContext, out PropertyValue.ValueType type)
        {
            var ttype = PropertyValue.ValueType.Null;
            if (scopeContext == null)
            {
                type = ttype;
                return new IInstruction[0];
            }

            var instructions = scopeContext.effect().Select(x => { return ParseEffect(x, out ttype); })
                .Where(e => e != null).ToArray();
            type = ttype;
            return instructions;
        }

        public InterpolatedString ParseInterpolatedString(MoiraiParser.StringContext? stringContext)
        {
            if (stringContext == null || stringContext.stringContent().Length == 0)
                return new InterpolatedString("", Array.Empty<IValue>());

            List<IValue> paths = new();
            string result = "";
            foreach (var part in stringContext.stringContent())
            {
                if (part.TEXT() != null)
                    result += part.TEXT().GetText();
                else
                {
                    result += $"{{{paths.Count}}}";
                    paths.Add(ParseExpr(part.expr())!);
                }
            }

            // var str = stringContext.GetText().TrimQuotes();
            // List<IValue> paths = new();
            // string result = "";
            // int i = -1;
            // var prev = i + 1;
            //
            // while (i < str.Length)
            // {
            //     i = str.IndexOf('{', i + 1);
            //     if (i == -1)
            //         break;
            //
            //     int j = str.IndexOf('}', i + 1);
            //     if (j == -1)
            //         throw new System.NotImplementedException(
            //             $"Missing curly brace in string: {str}, opening brace at {i}");
            //
            //     var pathStr = str.Substring(i + 1, j - i - 1);
            //     var path = StoryParser.ParseExpr(this, pathStr,
            //         stringContext.Start.Line - 1 /* +1 somewhere in the pipeline */,
            //         stringContext.Start.Column + i + 1 + /*quote*/ 1, out _);
            //     paths.Add(path!);
            //     // Console.WriteLine($"'{pathStr}'");
            //     if (i > prev)
            //         result += str.Substring(prev, i - prev);
            //     result += $"{{{paths.Count - 1}}}";
            //     i = j;
            //     prev = i + 1;
            // }
            //
            // if (prev < str.Length)
            //     result += (str.Substring(prev));
            // // Console.WriteLine($"res:'{result}'");
            var interpolatedString = new InterpolatedString(result, paths.ToArray());
            return interpolatedString;
        }

        public IValue? ParseExpr(MoiraiParser.ExprContext context)
        {
            return ParseExpr(context, out var _);
        }

        public IValue? ParseExpr(MoiraiParser.ExprContext context, out PropertyValue.ValueType type)
        {
            if (context == null)
            {
                type = PropertyValue.ValueType.Null;
                return null;
            }
            if (context.@if() != null)
                return ParseIf(context.@if(), out type);
            if (context.match() != null)
                return ParseMatch(context.match(), out type);
            if (context.value() != null)
            {
                return ParseValue(context.value(), out type);
                // ComputedValue v = ParseValue(context.value(0), PropertyValue.TypeBool);
            }

            if (context.paren_expr != null)
                return ParseExpr(context.paren_expr, out type);

            string op = context.op.Text;
            // left, alive
            IValue leftPath = ParseExpr(context.left, out var leftType)!;

            // right, true or $x -  not alive or $x.alive
            IValue rightValue = ParseExpr(context.right, out var rightType)!;

            BinaryOperator.Operator pop;
            switch (op)
            {
                case "and":
                    pop = BinaryOperator.Operator.And;
                    type = PropertyValue.TypeBool;
                    break;
                case "or":
                    pop = BinaryOperator.Operator.Or;
                    type = PropertyValue.TypeBool;
                    break;
                case "??":
                    pop = BinaryOperator.Operator.Coalesce;
                    type = rightType;
                    break;
                case "=":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.Equals;

                    if (leftPath is PropertyPath {Nested: false} p &&
                        (p.Segments == null || p.Segments[0].Property == Database.PropType) &&
                        rightValue is Literal l &&
                        l.Value.Type == PropertyValue.TypeEntityType)
                    {
                        type = l.Value.Type;
                        return new IsOfType(leftPath, l.Value.TypeId);
                    }

                    break;
                case "!=":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.NotEquals;
                    break;
                case "+":
                    type = rightType;
                    pop = BinaryOperator.Operator.Add;
                    break;
                case "-":
                    type = rightType;
                    pop = BinaryOperator.Operator.Sub;
                    break;
                case "/":
                    type = rightType;
                    pop = BinaryOperator.Operator.Div;
                    break;
                case "*":
                    type = rightType;
                    pop = BinaryOperator.Operator.Mul;
                    break;
                case "%":
                    type = PropertyValue.TypeNumber;
                    pop = BinaryOperator.Operator.Mod;
                    break;
                case ">":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.Gt;
                    break;
                case "<":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.Lt;
                    break;
                case ">=":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.Ge;
                    break;
                case "<=":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.Le;
                    break;
                default:
                    type = default;
                    return (IValue?) AddError(ErrorCode.UnknownExpressionOperator, context, op);
            }

            return new BinaryOperator(pop, leftPath, rightValue);
        }

        public object AddError(ErrorCode code, ParserRuleContext loc, string msg)
        {
            Errors.Add(new Error(code, loc, Parser.TokenStream.GetText(loc) + ": " + msg, Offset));
            // to avoid warnings in a case where the parsing is already compromised
            return null!;
        }

        public object? AddError(ErrorCode code, ITerminalNode loc, string msg)
        {
            Errors.Add(new Error(code, loc, msg, Offset));
            return null;
        }

        public override object? VisitCall(MoiraiParser.CallContext context)
        {
            throw new NotImplementedException();
        }

        public override object? VisitExpr(MoiraiParser.ExprContext context)
        {
            throw new NotImplementedException();
        }

        public override object? VisitPath(MoiraiParser.PathContext context)
        {
            throw new NotImplementedException();
        }

        struct PathParser(AstVisitor astVisitor, MoiraiParser.PathContext context)
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
                        var ctx = new FunctionDescriptor.ParseContext(astVisitor, dotPropertyContext.call());
                        var call = astVisitor.ParseUserFunctionCall(astVisitor, fd, ctx, out type);
                        path.AddCall(call);
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

        private void ParseProperty(ref PropertyPath path, MoiraiParser.PathContext context,
            EntityType owningType, out PropertyValue.ValueType type)
        {
            // TODO path without var isn't implemented ? prop1.prop2 ?

            PathParser pathParser = new(this, context);
            type = default;
            if (context.property_id() is { } rootProp)
            {
                pathParser.ParseProperty(ref path, rootProp, owningType, out type);
                
            }

            if(context.dot_property(0) != null)
                pathParser.Rec(ref path, 0, owningType, out type);
        }


        public PropertyPath ParsePath(MoiraiParser.PathContext context, out PropertyValue.ValueType type)
        {
            // if (context.ID().Length > 1)
            // throw new Exception("expected two parts, got " + (context.ID().Length + 1));

            ITerminalNode? singletonId = context.var_id_read()?.SINGLETON_ID();
            if (singletonId != null)
            {
                string typeName = singletonId.GetText().Substring(1);
                EntityType singletonType = Database.GetEntityType(typeName);
                if (!singletonType.Id.IsValid)
                {
                    AddError(ErrorCode.UnknownEntityType, singletonId, typeName);
                    type = default;

                    return default;
                }
                Linker?.LinkType(new(singletonId), singletonType.Id);

                // TODO chained singleton #Time.x.y
                var path = new PropertyPath(singletonType.Id);
                ParseProperty(ref path, context, singletonType, out type);
                return path;
            }

            int variableIndex;
            ITerminalNode? varName = context.var_id_read()?.VAR_ID();
            if (varName != null)
            {
                VariableDeclaration decl;
                if (!int.TryParse(varName.GetText().Substring(1), out variableIndex))
                {
                    variableIndex = _current.GetVariableIndexByName(varName.GetText(), out decl);
                    if (variableIndex == -1)
                    {
                        AddError(ErrorCode.VariableNotDeclared, context, varName.GetText());
                        type = default;
                        return new PropertyPath();
                    }
                }
                else
                    decl = _current[variableIndex];

                type = _current[variableIndex].Type;
                Linker?.LinkVariable(new FileRange(varName), decl);
                if (context.dot_property().Length == 0)
                {
                    return new PropertyPath(variableIndex);
                }
            }
            else
                variableIndex = _current.Count - 1;

            {
                EntityType? etype = Database.GetEntityType(_current[variableIndex].Type);
                var path = new PropertyPath(variableIndex);
                ParseProperty(ref path, context,  etype, out type);
                return path;
            }
        }
    }

    public static ITerminalNode GetTypeTerminal(MoiraiParser.TypeContext type)
    {
        return type.TYPE_ID() ?? type.ID();
    }
}

internal static class ParsingExtensions
{
    public static string TrimQuotes(this string s) => s.Trim('"', '\'');
    public static string GetString(this MoiraiParser.StringContext context) => context.GetText().TrimQuotes();
}
