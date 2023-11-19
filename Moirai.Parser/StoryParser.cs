using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Moirai.Parser;

// public class InstructionFunctionDescriptor : IFunctionDescriptor
// {
//     public string FuncName { get; }
//     private readonly Func<StoryParser.AstVisitor,CallContext, IInstructionCall> _parse;
//
//     public InstructionFunctionDescriptor(string funcName, Func<StoryParser.AstVisitor, CallContext, IInstructionCall> parse)
//     {
//         FuncName = funcName;
//         _parse = parse;
//     }
//
//     public IInstruction ParseInstruction(StoryParser.AstVisitor parser, CallContext args)
//     {
//         var c =  _parse(parser, args);
//         c.FunctionDescriptor = this;
//         return c;
//     }
//
//     public string Print(StoryPrinter printer, IValueCall call)
//     {
//         return $"{FuncName} {string.Join(", ", call.GetArgs().Select(a => printer.Print(a)))}";
//     }
// }
public class FunctionDescriptor : IFunctionDescriptor
{
    public record ParseContext(StoryParser.AstVisitor Visitor, MoiraiParser.CallContext CallContext)
    {
        public int ParseVariable()
        {
            Visitor.DeclareVar(this.CallContext.VAR_ID().GetText(), this.CallContext.VAR_ID().Symbol, out var varIndex);
            return varIndex;
        }

        public EntityType ParseEntityType()
        {
            var type = CallContext.expr(0).GetText().TrimQuotes();
            EntityType typeId = Visitor._database.GetEntityType(type);
            if (typeId.Id == EntityTypeId.Null)
            {
                Visitor.AddError(StoryParser.ErrorCode.UnknownEntityType, CallContext.expr(0), $"'{type}'");
            }

            return typeId;
        }

        public string GetText(RuleContext expr) => Visitor.Parser.TokenStream.GetText(expr);

        public IInstruction[]? ParseScope(bool autoCleanupVariableDeclarations)
        {
            return Visitor.ParseScope(CallContext.scope(), autoCleanupVariableDeclarations);
        }
    }

    public delegate IValueCall ParseCallDelegate(ParseContext context);

    public string FuncName { get; }
    public bool ExpectVariable { get; }
    private readonly ParseCallDelegate _parse;

    public FunctionDescriptor(string funcName, bool expectVariable, ParseCallDelegate parse)
    {
        FuncName = funcName;
        ExpectVariable = expectVariable;
        _parse = parse;
    }

    public IValueCall Parse(StoryParser.AstVisitor parser, MoiraiParser.CallContext call)
    {
        var c = _parse(new ParseContext(parser, call));
        if (c != null)
            c.FunctionDescriptor = this;
        else
            throw new InvalidOperationException(parser.Parser.TokenStream.GetText(call));
        return c;
    }


    public string Print(StoryPrinter printer, IValueCall call)
    {
        return $"{FuncName}{(call.VariableIndex.HasValue ? $" ${call.VariableIndex.Value}:" : "")} {string.Join(", ", call.GetArgs(printer).Select(a => printer.Print(a)))}";
    }
}

public static class StoryParser
{
    private static FunctionDescriptor[] Functions = new FunctionDescriptor[]
    {
        new("create", true, ctx =>
        {
            var typeId = ctx.ParseEntityType();
            return new CreateEntity(ctx.ParseVariable(), typeId.Id,
                ctx.Visitor.ParseInterpolatedString(ctx.CallContext.expr(1)?.value()?.@string()));
        }),
        new("each", true,
            ctx => new AssignPick(ctx.ParseVariable(), ctx.Visitor.ParsePredicate(ctx.CallContext.expr()),
                CallType.Each, ctx.ParseScope(true))),
        new("pick", true,
            ctx => new AssignPick(ctx.ParseVariable(), ctx.Visitor.ParsePredicate(ctx.CallContext.expr()),
                CallType.Pick)),

        new("assert", false, ctx =>
            new AssertInstr(ctx.Visitor.ParseExpr(ctx.CallContext.expr(0)), ctx.GetText(ctx.CallContext.expr(0)))),
        new("assert_eq", false, ctx =>
            new AssertInstr(
                ctx.Visitor.ParseExpr(ctx.CallContext.expr(0)),
                ctx.Visitor.ParseExpr(ctx.CallContext.expr(1)),
                $"{ctx.Visitor.Parser.TokenStream.GetText(ctx.CallContext.expr(0))} = {ctx.GetText(ctx.CallContext.expr(1))}")),

        new("record", false, ctx =>
        {
            var stringContext = ctx.CallContext.expr(0).value().@string();
            var interpolatedString = ctx.Visitor.ParseInterpolatedString(stringContext);
            return new Record(interpolatedString);
        }),
        new("call", false, ctx =>
        {
            var arg = ctx.CallContext.expr(0);
            string? ruleName = arg.value()?.path()?.GetText() ?? arg.value()?.@string()?.GetString();
            if (ruleName == null)
            {
                ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext, "rule name");
            }

            var ruleIndex = ctx.Visitor._database.Actions.FindIndex(r => r.Name == ruleName);
            if (ruleIndex == -1)
            {
                ctx.Visitor.AddError(ErrorCode.UnknownRule, arg, ruleName);
            }

            int count = 1;
            if (ctx.CallContext.expr(1) != null)
            {
                count = int.Parse(ctx.CallContext.expr(1).GetText());
            }

            {
                return new CallRule(ruleIndex, count);
            }
        }),

        new("random", false, ctx =>
        {
            var arg = ctx.CallContext.expr(0);

            if (arg.value()?.TYPE_ID() != null)
            {
                if (!ctx.Visitor._database.GetEnumDefinition(arg.GetText(), out var enumDef))
                    ctx.Visitor.AddError(ErrorCode.UnknownEnum, arg, "");

                return new RandomEnum(enumDef.Index);
            }

            if (arg.value().number() != null)
            {
            }

            ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext, ctx.GetText(ctx.CallContext));
            return null!;
        }),
        new FunctionDescriptor("floor", false, ctx => new MathUnary(MathUnary.UnaryFunction.Floor, ctx.Visitor.ParseExpr(ctx.CallContext.expr(0)))),
        new FunctionDescriptor("round", false, ctx => new MathUnary(MathUnary.UnaryFunction.Round, ctx.Visitor.ParseExpr(ctx.CallContext.expr(0)))),
        new FunctionDescriptor("ceiling", false, ctx => new MathUnary(MathUnary.UnaryFunction.Ceiling, ctx.Visitor.ParseExpr(ctx.CallContext.expr(0)))),
        new FunctionDescriptor("clamp01", false, ctx => new MathUnary(MathUnary.UnaryFunction.Clamp01, ctx.Visitor.ParseExpr(ctx.CallContext.expr(0)))),
    };

    public interface IVisitor
    {
        List<Error> Errors { get; }
        MoiraiParser Parser { get; set; }
        (int offsetLine, int offsetColumn) offset { get; set; }
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
        TypeNameMustStartWithUpperCase,
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
        MissingVariable
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

        public override string ToString() => $"M{(int)Code}: {Code} {Line}:{Col}: {Message}";
    }

    class Listener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
    {
        private readonly List<Error> _errors;
        private readonly (int offsetLine, int offsetColumn) _offset;

        public Listener(List<Error> errors, (int offsetLine, int offsetColumn)? offset)
        {
            _errors = errors;
            _offset = offset ?? (0,0);
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(ErrorCode.Lexer, line + _offset.offsetLine, charPositionInLine + _offset.offsetColumn, "Lexer:" + msg));
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(ErrorCode.Parser, line + _offset.offsetLine, charPositionInLine + _offset.offsetColumn, "Parser:" + msg));
        }
    }

    internal static IValue? ParseExpr(AstVisitor visitor, string s, int offsetLine, int offsetColumn, out List<Error> errors)
    {
        var prevOffset = visitor.offset;
        SetupParser(s, out var parser, visitor, (offsetLine, offsetColumn));
        var r = parser.expr();
        var propertyPath = visitor.ParseExpr(r);
        errors = visitor.Errors;
        visitor.offset = prevOffset;
        return propertyPath;
    }

    public static Database Parse(string s, out List<Error> errors)
    {
        var db = new Database();
        var visitor = new AstVisitor(db);
        SetupParser(s, out var parser, visitor);
        var r = parser.r();
        r.Accept(visitor);
        errors = visitor.Errors;
        return db;
    }

    public static void SetupParser(string s, out MoiraiParser parser, IVisitor visitor,
        (int offsetLine, int offsetColumn)? offset = null)
    {
        var lexer = new moirai_lexer(CharStreams.fromString(s /*.TrimStart('\r', '\n', ' ')*/));
        var tokens = new CommonTokenStream(lexer);

        parser = new MoiraiParser(tokens);
        visitor.Parser = parser;
        visitor.offset = offset ?? (0,0);
        var listener = new Listener(visitor.Errors, offset);
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);
    }

    public class AstVisitor : MoiraiParserBaseVisitor<object?>, IVisitor
    {
        public (int offsetLine, int offsetColumn) offset { get; set; }

        private List<string> _variables = new();
        private List<Error> _errors = new();
        public List<Error> Errors => _errors;
        public MoiraiParser Parser { get; set; }
        protected override object? DefaultResult => null;

        public readonly Database _database;

        // private int _implicitVariableIndex = -1;
        public AstVisitor(Database database)
        {
            _database = database;
        }

        public override object? VisitType_definition(MoiraiParser.Type_definitionContext context)
        {
            if (context.TYPE_ID() == null)
                return AddError(ErrorCode.TypeNameMustStartWithUpperCase, context, context.GetText());

            var typeName = context.TYPE_ID().GetText();
            DeclareEntityType(typeName);
            return null;
        }

        public uint DeclareEntityType(string typeName)
        {
            var id = (uint)_database.Types.Count;
            _database.Types.Add(new EntityType(typeName, id));
            return id;
        }

        public override object? VisitProp_definition(MoiraiParser.Prop_definitionContext context)
        {
            var propName = context.ID(0).GetText();
            if (_database.GetPropertyId(propName).Id != 0)
                return AddError(ErrorCode.DuplicatePropertyDefinition, context, propName);

            PropertyValue.ValueType type = ParseType(context.ID(1) ?? context.TYPE_ID());
            _database.Properties.Add(new PropertyDefinition(propName, (uint)_database.Properties.Count, type));
            return null;
        }

        private PropertyValue.ValueType ParseType(ITerminalNode id)
        {
            switch (id.GetText())
            {
                case "bool": return PropertyValue.TypeBool;
                case "ref": return PropertyValue.TypeRef;
                case "number": return PropertyValue.TypeNumber;
                case "float": return PropertyValue.TypeFloat;
                case "string": return PropertyValue.TypeString;
                default:
                    if (_database.GetEnumDefinition(id.GetText(), out EnumDefinition enumDefinition))
                        return PropertyValue.TypeEnum(enumDefinition.Index);
                    var entityType = _database.GetEntityType(id.GetText());
                    if (entityType.Id.IsValid)
                        return entityType.RefType;
                    AddError(ErrorCode.UnknownPropertyType, id, id.GetText());
                    return default;
            }
        }

        public override object? VisitEnum_definition(MoiraiParser.Enum_definitionContext context)
        {
            EnumDefinition en = new(new EnumDefinitionId((ushort)_database.Enums.Count), context.TYPE_ID(0).GetText(),
                context.TYPE_ID().Skip(1).Select(v => v.GetText()).ToList());
            _database.Enums.Add(en);
            return null;
        }

        public override object? VisitAction(MoiraiParser.ActionContext context)
        {
            string actionId = context.ID().GetText();
            // bool isStartAction = context.AT() != null;
            //Console.WriteLine("@ " + actionId);
            _variables.Clear();
            IFilter? f = null;
            if (context.filter() != null)
            {
                var p = context.filter();
                switch (p.ID(0).GetText())
                {
                    case "start":
                        f = new FilterAtStart();
                        break;
                    case "every":
                    {
                        var x = int.Parse(p.occurence.Text);
                        var y = int.Parse(p.years.Text);
                        f = new FilterExactlyXEveryYYears(x, y);
                        break;
                    }
                    case "per":
                    {
                        var x = int.Parse(p.occurence.Text);
                        var y = int.Parse(p.years.Text);
                        f = new FilterProbabilityXPerYears(x, y);
                        break;
                    }
                }
            }


            var cats = ParseCategories(context.categories());

            var action = new Action(_database.Actions.Count + 1, actionId, false, f, cats);
            foreach (MoiraiParser.EffectContext effectContext in context.effect())
            {
                // if (effectContext.comment() != null)
                //     continue;
                var effect = ParseEffect(effectContext);
                if (effect == null)
                {
                    AddError(ErrorCode.NullEffect, effectContext, effectContext.GetText());
                    continue;
                }

                action.Effects.Add(effect);
            }

            _database.Actions.Add(action);
            return null;
        }

        private CategoryId[] ParseCategories(MoiraiParser.CategoriesContext tagIds)
        {
            CategoryId[] tags = new CategoryId[tagIds.ID().Length];
            var nodes = tagIds;
            for (var index = 0; index < nodes.ID().Length; index++)
            {
                var cat = tagIds.ID(index);
                tags[index] = _database.GetCategoryId(cat.GetText());
            }

            return tags;
        }

        public override object? VisitEvent(MoiraiParser.EventContext context)
        {
            string actionId = context.ID().GetText();
            //Console.WriteLine("@ " + actionId);
            var categories = ParseCategories(context.categories());
            var action = new Action(_database.Events.Count + 1, actionId, true, null, categories);
            _variables.Clear();

            using (new VariableDeclarationScope(this, true)) ;
            if(context.when() != null)
                DeclareVar("$old", null, out var oldIndex);
            DeclareVar("$new", null, out var newIndex);
            if(context.when_created() is { } createdContext)
            {
                EntityType type = _database.GetEntityType(createdContext.TYPE_ID().GetText());
                if (!type.Id.IsValid)
                    AddError(ErrorCode.UnknownPropertyType, createdContext, createdContext.TYPE_ID().GetText());
                action.When = (Action.WhenType.Created, type.Id, ParsePredicate(createdContext.expr()));
            }
            else if(context.when() is {} whenContext)
            {
                EntityType type = _database.GetEntityType(whenContext.TYPE_ID().GetText());
                if (!type.Id.IsValid)
                    AddError(ErrorCode.UnknownPropertyType, whenContext, whenContext.TYPE_ID().GetText());
                action.When = (Action.WhenType.Changed,type.Id, ParsePredicate(whenContext.expr()));
            }

            _database.Events.Add(action);
            foreach (var effectContext in context.effect())
            {
                // if (effectContext.comment() != null)
                // continue;
                var effect = ParseEffect(effectContext);
                if (effect != null)
                    action.Effects.Add(effect);
            }

            return null;
        }

        private IInstruction ParseEffect(MoiraiParser.EffectContext effectContext)
        {
            if (effectContext.expr() != null)
            {
                var value = ParseExpr(effectContext.expr());
                if (value != null)
                    return new CallInstruction(value);
            }

            if (effectContext.var() != null)
                return ParseVar(effectContext.var());
            if (effectContext.set() != null)
                return ParseSet(effectContext.set());


            AddError(ErrorCode.Exception, effectContext, "NULL");
            return new SetProperty(default, null, false);
        }

        private bool _parsingMatchCase = false;

        private IValue ParseMatch(MoiraiParser.MatchContext match)
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
            for (int i = 0; i < match.match_case().Length; i++)
            {
                var caseCtx = match.match_case(i);
                _parsingMatchCase = true;
                IValue[] caseValues;
                try
                {
                    caseValues = caseCtx.value().Select(ParseValue).ToArray();
                }
                finally
                {
                    _parsingMatchCase = false;
                }

                using var _ = new VariableDeclarationScope(this, true);
                var instrs = caseCtx.scope() == null
                    ? new IInstruction[] { ParseEffect(caseCtx.effect()) }
                    : ParseScope(caseCtx.scope(), false);
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
                        w = (int)((Literal)caseValues[0]).Value.IntValue;
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

        private If ParseIf(MoiraiParser.IfContext @if)
        {
            return new If(ParseExpr(@if.cond), ParseScope(@if.then, true),
                @if.@else == null ? Array.Empty<IInstruction>() : ParseScope(@if.@else, true));
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
            throw new System.NotImplementedException();
        }

        public override object? VisitSet(MoiraiParser.SetContext context)
        {
            throw new System.NotImplementedException();

            return null;
        }

        private SetProperty ParseVar(MoiraiParser.VarContext context)
        {
            var name = context.VAR_ID();
            DeclareVar(name.GetText(), name.Symbol, out var varIndex);
            var expr = ParseExpr(context.expr());
            return new SetProperty(new PropertyPath(varIndex), expr, true);
        }

        private SetProperty ParseSet(MoiraiParser.SetContext context)
        {
            var left = ParsePath(context.path());
            var right = ParseExpr(context.expr()); //, left.Property);
            return new SetProperty(left, right, false);
        }

        private IValue ParseValue(MoiraiParser.ValueContext value)
        {
            if (_parsingMatchCase && value.path()?.GetText() == "_")
                return MatchAnyValue.Instance;
            if (value.TYPE_ID() != null)
            {
                var type = _database.GetEntityType(value.TYPE_ID().GetText());
                if (!type.Id.IsValid)
                    AddError(ErrorCode.UnknownPropertyType, value, value.TYPE_ID().GetText());
                return new Literal(type.Id);
            }

            if (value.call() != null)
            {
                return ParseCall(value.call());
            }

            if (value.path() != null)
            {
                PropertyPath path = ParsePath(value.path());
                return path;
            }

            if (value.@string() != null)
                return ParseInterpolatedString(value.@string());
            if (value.NULL() != null)
                return new Literal(EntityId.Null);
            if (value.number() != null)
            {
                if (value.number().NUMBER_FLOAT() != null)
                    return new Literal(float.Parse(value.number().NUMBER_FLOAT().GetText()));
                return new Literal(int.Parse(value.number().GetText()));
            }
            if (value.@bool() != null)
                return new Literal(value.@bool().TRUE() != null);

            if (value.enum_value() != null)
            {
                var enumType = value.enum_value().TYPE_ID(0);
                if (!_database.GetEnumDefinition(enumType.GetText(), out var enumDef))
                    return (AddError(ErrorCode.UnknownEnum, enumType, enumType.GetText()) as IValue)!;

                var enumValue = value.enum_value().TYPE_ID(1);
                if (!enumDef.GetValueFromName(enumValue.GetText(), out var val))
                    return (AddError(ErrorCode.UnknownEnumValue, enumValue,
                        enumValue.GetText() + " in enum " + enumDef.Name) as IValue)!;

                return new Literal(val);
            }

            throw new ArgumentOutOfRangeException();
        }

        public bool DeclareVar(string variable, IToken contextStart, out int varIndex)
        {
            if ((varIndex = _variables.IndexOf(variable)) != -1)
            {
                // AddError(ErrorCode.DuplicateVariableDefinition,  contextStart, " Duplicate variable " + variable);
                // varIndex = 0;
                return true;
            }

            _variables.Add(variable);
            varIndex = _variables.Count - 1;
            return true;
        }

        struct VariableDeclarationScope : IDisposable
        {
            private readonly AstVisitor _astVisitor;
            private readonly bool _autoCleanup;
            private readonly int _count;

            public VariableDeclarationScope(AstVisitor astVisitor, bool autoCleanup)
            {
                _astVisitor = astVisitor;
                _autoCleanup = autoCleanup;
                _count = astVisitor._variables.Count;
            }

            public void Dispose()
            {
                if (_autoCleanup)
                    Cleanup();
            }

            public void Cleanup()
            {
                _astVisitor._variables.RemoveRange(_count, _astVisitor._variables.Count - _count);
            }
        }

        private IValue ParseCall(MoiraiParser.CallContext context)
        {
            var funcName = context.ID().GetText();
            var f = Functions.FirstOrDefault(f => f.FuncName == funcName);
            if (f != null)
            {
                return f.Parse(this, context);
            }

            return (AddError(ErrorCode.UnknownInstruction, context, funcName) as IValue)!;
        }

        public IInstruction[] ParseScope(MoiraiParser.ScopeContext scopeContext, bool autoCleanupVariableDeclarations)
        {
            using var vs = new VariableDeclarationScope(this, autoCleanupVariableDeclarations);
            if (scopeContext == null)
                return Array.Empty<IInstruction>();
            return scopeContext.effect().Select(ParseEffect).Where(e => e != null).ToArray();
        }

        public InterpolatedString ParseInterpolatedString(MoiraiParser.StringContext? stringContext)
        {
            if (stringContext == null)
                return new InterpolatedString("", Array.Empty<IValue>());
            var str = stringContext.GetText().TrimQuotes();
            List<IValue> paths = new();
            string result = "";
            int i = -1;
            var prev = i + 1;

            while (i < str.Length)
            {
                i = str.IndexOf('{', i + 1);
                if (i == -1)
                    break;

                int j = str.IndexOf('}', i + 1);
                if (j == -1)
                    throw new System.NotImplementedException(
                        $"Missing curly brace in string: {str}, opening brace at {i}");

                var pathStr = str.Substring(i + 1, j - i - 1);
                var path = StoryParser.ParseExpr(this, pathStr, stringContext.Start.Line - 1 /* +1 somewhere in the pipeline */, stringContext.Start.Column + i + 1 +/*quote*/  1, out _);
                paths.Add(path!);
                // Console.WriteLine($"'{pathStr}'");
                if (i > prev)
                    result += str.Substring(prev, i - prev);
                result += $"{{{paths.Count - 1}}}";
                i = j;
                prev = i + 1;
            }

            if (prev < str.Length)
                result += (str.Substring(prev));
            // Console.WriteLine($"res:'{result}'");
            var interpolatedString = new InterpolatedString(result, paths.ToArray());
            return interpolatedString;
        }

        public IValue? ParseExpr(MoiraiParser.ExprContext context)
        {
            if (context.@if() != null)
                return ParseIf(context.@if());
            if (context.match() != null)
                return ParseMatch(context.match());
            if (context.value() != null)
            {
                return ParseValue(context.value());
                // ComputedValue v = ParseValue(context.value(0), PropertyValue.TypeBool);
            }

            if (context.paren_expr != null)
                return ParseExpr(context.paren_expr);

            string op = context.op.Text;
            // left, alive
            IValue leftPath = ParseExpr(context.left)!;

            // right, true or $x -  not alive or $x.alive
            IValue rightValue = ParseExpr(context.right)!;

            BinaryOperator.Operator pop;
            switch (op)
            {
                case "=":
                    pop = BinaryOperator.Operator.Equals;

                    if (leftPath is PropertyPath { Nested: false } p && p.Property == Database.PropType &&
                        rightValue is Literal l &&
                        l.Value.Type == PropertyValue.TypeEntityType)
                    {
                        return new IsOfType(leftPath, l.Value.TypeId);
                    }

                    break;
                case "!=":
                    pop = BinaryOperator.Operator.NotEquals;
                    break;
                case "+":
                    pop = BinaryOperator.Operator.Add;
                    break;
                case "-":
                    pop = BinaryOperator.Operator.Sub;
                    break;
                case "/":
                    pop = BinaryOperator.Operator.Div;
                    break;
                case "*":
                    pop = BinaryOperator.Operator.Mul;
                    break;
                case ">":
                    pop = BinaryOperator.Operator.Gt;
                    break;
                case "<":
                    pop = BinaryOperator.Operator.Lt;
                    break;
                case ">=":
                    pop = BinaryOperator.Operator.Ge;
                    break;
                case "<=":
                    pop = BinaryOperator.Operator.Le;
                    break;
                default: return (IValue?)AddError(ErrorCode.UnknownExpressionOperator, context, op);
            }

            return new BinaryOperator(pop, leftPath, rightValue);
        }

        public object AddError(ErrorCode code, ParserRuleContext loc, string msg)
        {
            Errors.Add(new Error(code, loc, Parser.TokenStream.GetText(loc) + ": " + msg, offset));
            // to avoid warnings in a case where the parsing is already compromised
            return null!;
        }

        public object? AddError(ErrorCode code, ITerminalNode loc, string msg)
        {
            Errors.Add(new Error(code, loc, msg, offset));
            return null;
        }

        public override object? VisitCall(MoiraiParser.CallContext context)
        {
            throw new System.NotImplementedException();
        }

        public override object? VisitExpr(MoiraiParser.ExprContext context)
        {
            throw new System.NotImplementedException();
        }

        public override object? VisitPath(MoiraiParser.PathContext context)
        {
            throw new System.NotImplementedException();
        }


        public PropertyPath ParsePath(MoiraiParser.PathContext context)
        {
            if (context.ID().Length > 1)
                throw new Exception("expected two parts, got " + (context.ID().Length + 1));

            var propertyId = PropertyId.Null;
            if (context.ID(0) != null)
            {
                var propertyName = context.ID(0)?.GetText();
                propertyId = _database.GetPropertyId(propertyName.ToLowerInvariant());
                if (!propertyId.IsValid)
                {
                    AddError(ErrorCode.UnknownProperty, context.ID(0), propertyName);
                    return default;
                }
            }

            var singletonId = context.SINGLETON_ID();
            if (singletonId != null)
            {
                var typeName = singletonId.GetText().Substring(1);
                var singletonType = _database.GetEntityType(typeName);
                if (!singletonType.Id.IsValid)
                {
                    AddError(ErrorCode.UnknownEntityType, singletonId, typeName);
                    return default;
                }

                return new PropertyPath(singletonType.Id, propertyId);
            }

            int variableIndex = -1;
            var varId = context.VAR_ID();
            if (varId != null)
            {
                if (!int.TryParse(varId.GetText().Substring(1), out variableIndex))
                {
                    variableIndex = _variables.IndexOf(varId.GetText());
                    if (variableIndex == -1)
                    {
                        AddError(ErrorCode.VariableNotDeclared, context, varId.GetText());
                        return new PropertyPath();
                    }
                }

                if (context.ID().Length == 0)
                    return new PropertyPath(variableIndex);
            }

            return new PropertyPath(variableIndex, propertyId);
        }
    }
}

internal static class ParsingExtensions
{
    public static string TrimQuotes(this string s) => s.Trim('"', '\'');
    public static string GetString(this MoiraiParser.StringContext context) => context.STRING().GetText().TrimQuotes();
}
