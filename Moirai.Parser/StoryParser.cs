using System.Reflection.Metadata;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Tree;
using Moirai.Parser;

public static class StoryParser
{
    public interface IVisitor
    {
        List<Error> Errors { get; }
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
        MatchAnyValueMustBeLast
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

        public Error(ErrorCode code, ITerminalNode loc, string message)
        {
            Code = code;
            Line = loc.Symbol.Line;
            Col = loc.Symbol.Column;
            Message = message;
            LineEnd = loc.Symbol.Line;
            ColEnd = loc.Symbol.Column + loc.Symbol.Text.Length;
        }

        public Error(ErrorCode code, ParserRuleContext loc, string message)
        {
            Code = code;
            Line = loc.Start.Line;
            Col = loc.Start.Column;
            Message = message;
            LineEnd = loc.Stop.Line;
            ColEnd = loc.Stop.Column;
        }

        public override string ToString() => $"M{(int)Code}: {Code} {Line}:{Col}: {Message}";
    }

    class Listener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
    {
        private readonly List<Error> _errors;

        public Listener(List<Error> errors)
        {
            _errors = errors;
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(ErrorCode.Lexer, line, charPositionInLine, "Lexer:" + msg));
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(ErrorCode.Parser, line, charPositionInLine, "Parser:" + msg));
        }
    }

    internal static IValue? ParseExpr(AstVisitor visitor, string s, out List<Error> errors)
    {
        SetupParser(s, out var parser, visitor);
        var r = parser.expr();
        var propertyPath = visitor.ParseExpr(r);
        errors = visitor.Errors;
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

    public static void SetupParser(string s, out MoiraiParser parser, IVisitor visitor)
    {
        var lexer = new moirai_lexer(CharStreams.fromString(s /*.TrimStart('\r', '\n', ' ')*/));
        var tokens = new CommonTokenStream(lexer);
        parser = new MoiraiParser(tokens);
        var listener = new Listener(visitor.Errors);
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);
    }

    public class AstVisitor : MoiraiParserBaseVisitor<object?>, IVisitor
    {
        private List<string> _variables = new();
        private List<Error> _errors = new();
        public List<Error> Errors => _errors;
        protected override object? DefaultResult => null;

        private readonly Database _database;

        // private int _implicitVariableIndex = -1;
        public AstVisitor(Database database)
        {
            _database = database;
        }

        public override object? VisitTag_definition(MoiraiParser.Tag_definitionContext context)
        {
            if (!_database.DeclareTag(context.TAG_ID().GetText()))
                AddError(ErrorCode.DuplicateTagDefinition, context.TAG_ID(), context.TAG_ID().GetText());
            return base.VisitTag_definition(context);
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
                case "string": return PropertyValue.TypeString;
                default:
                    if (_database.GetEnumDefinition(id.GetText(), out EnumDefinition enumDefinition))
                        return PropertyValue.TypeEnum(enumDefinition.Index);

                    AddError(ErrorCode.UnknownPropertyType, id, id.GetText());
                    return default;
            }
        }

        public override object? VisitEnum_definition(MoiraiParser.Enum_definitionContext context)
        {
            EnumDefinition en = new((ushort)_database.Enums.Count, context.TYPE_ID(0).GetText(),
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
                if (effectContext.comment() != null)
                    continue;
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
            foreach (var whenTag in context.when_tag())
            {
                if (!_database.GetTagId(whenTag.TAG_ID()?.GetText(), out var tagId))
                    AddError(ErrorCode.UnknownTag, whenTag.TAG_ID(), whenTag.TAG_ID().GetText());
                action.WhenTags.Add(tagId);
            }

            foreach (var whenContext in context.when())
            {
                action.Whens.Add(ParseWhen(whenContext));
            }

            _database.Events.Add(action);
            foreach (var effectContext in context.effect())
            {
                if (effectContext.comment() != null)
                    continue;
                var effect = ParseEffect(effectContext);
                action.Effects.Add(effect);
            }

            return null;
        }

        private IInstruction ParseEffect(MoiraiParser.EffectContext effectContext)
        {
            if (effectContext.call_assign() != null)
                return ParseCall(effectContext.call_assign());
            if (effectContext.var() != null)
                return ParseVar(effectContext.var());
            if (effectContext.set() != null)
                return ParseSet(effectContext.set());
            if (effectContext.@if() != null)
                return ParseIf(effectContext.@if());
            if (effectContext.match() != null)
                return ParseMatch(effectContext.match());

            AddError(ErrorCode.Exception, effectContext, "NULL");
            return new SetProperty(default, null, false, default);
        }

        private bool _parsingMatchCase = false;

        private IInstruction ParseMatch(MoiraiParser.MatchContext match)
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
                    : ParseScope(caseCtx.scope());
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
            return new If(ParseExpr(@if.cond), ParseScope(@if.then),
                @if.@else == null ? Array.Empty<IInstruction>() : ParseScope(@if.@else));
        }

        private AssignPick ParseWhen(MoiraiParser.WhenContext context)
        {
            var exprs = context.expr();
            var predicate = exprs.Length == 1
                ? ParseExpr(exprs[0])!
                : new And(exprs.Select(x => ParseExpr(x)).Where(e => e != null).Cast<IValue>().ToList());
            var variableIndex = 0;
            {
                var variable = context.VAR_ID()?.GetText() ?? _variables.Count.ToString();
                if (!DeclareVar(variable, context.VAR_ID()?.Symbol ?? context.Start, out variableIndex))
                {
                }
            }
            return new AssignPick(variableIndex, predicate, CallType.When);
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
            PropertyValue.ValueType type = context.COLON() != null
                ? ParseType(context.ID() ?? context.TYPE_ID())
                : default;
            var expr = ParseExpr(context.expr());
            return new SetProperty(new PropertyPath(varIndex), expr, true, type);
        }

        private SetProperty ParseSet(MoiraiParser.SetContext context)
        {
            var left = ParsePath(context.path());
            var right = ParseExpr(context.expr()); //, left.Property);
            return new SetProperty(left, right, false, default);
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
                var call = value.call();
                var funcName = call.ID()?.GetText();
                switch (funcName)
                {
                    case "random":
                    {
                        var arg = call.expr(0);
                        if (arg.value()?.path() != null)
                        {
                            if (Enum.TryParse(arg.GetText(), true, out RandomName.NameType nt))
                                return new RandomName(nt);
                        }
                        if (arg.value()?.TYPE_ID() != null)
                        {
                            if (!_database.GetEnumDefinition(arg.GetText(), out var enumDef))
                                return (AddError(ErrorCode.UnknownEnum, arg, arg.GetText()) as IValue)!;

                            return new RandomEnum(enumDef.Index);
                        }

                        if (arg.value().number() != null)
                        {
                        }

                        return (AddError(ErrorCode.MissingArgument, value.call(), value.GetText()) as IValue)!;
                    }
                    default:
                        AddError(ErrorCode.UnknownCall, value, funcName);
                        return default!;
                }
            }

            if (value.path() != null)
            {
                PropertyPath path = ParsePath(value.path());
                return path;
            }

            if (value.@string() != null)
                return ParseInterpolatedString(value.@string().GetString());
            if (value.NULL() != null)
                return new Literal(EntityId.Null);
            if (value.number() != null)
                return new Literal(int.Parse(value.number().GetText()));
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

        private bool DeclareVar(string variable, IToken contextStart, out int varIndex)
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
                if(_autoCleanup)
                    Cleanup();
            }

            public void Cleanup()
            {
                _astVisitor._variables.RemoveRange(_count, _astVisitor._variables.Count - _count);
            }
        }

        private IInstruction ParseCall(MoiraiParser.Call_assignContext context)
        {
            var funcName = context.ID().GetText();
            // functions that do not declare a value
            switch (funcName)
            {
                case "assert":
                    return new AssertInstr(ParseExpr(context.expr(0)), context.expr(0).GetText());
                case "assert_eq":
                    return new AssertInstr(ParseExpr(context.expr(0)), ParseExpr(context.expr(1)),
                        context.expr(0).GetText() + " = " + context.expr(1).GetText());
                case "record":
                    var stringContext = context.expr(0).value().@string();
                    var interpolatedString = ParseInterpolatedString(stringContext.GetString());
                    return new FormatAction(interpolatedString);
                case "add_tag":
                    var path = ParsePath(context.expr(0).value().path());
                    var tag = context.expr(1).TAG_ID();
                    if (!_database.GetTagId(tag.GetText(), out var tagId))
                        return ((IInstruction)AddError(ErrorCode.UnknownTag, context.expr(0), $"'{tag.GetText()}'")!)!;
                    return new TagEntity(path, tagId);
            }

            int variableIndex = Math.Max(0, _variables.Count);
            using var varScope = new VariableDeclarationScope(this, false);
            if (context.VAR_ID() != null)
            {
                if (!DeclareVar(context.VAR_ID().GetText(), context.Start, out variableIndex))
                    return null;
            }
            else
            {
                if (!DeclareVar('$' + variableIndex.ToString(), context.Start, out variableIndex))
                    return null;
            }

            var instr = MakeInstruction(out var isScoped);
            if (isScoped)
                varScope.Cleanup();
            return instr;

            IInstruction MakeInstruction(out bool isScoped)
            {
                instr = null!;
                switch (funcName)
                {
                    case "call":
                    {
                        isScoped = false;
                        var arg = context.expr(0);
                        string? ruleName = arg.value()?.path()?.GetText() ?? arg.value()?.@string()?.GetString();
                        if (ruleName == null)
                        {
                            return (AddError(ErrorCode.MissingArgument, context, "rule name") as IInstruction)!;
                        }

                        var ruleIndex = _database.Actions.FindIndex(r => r.Name == ruleName);
                        if (ruleIndex == -1)
                        {
                            return (AddError(ErrorCode.UnknownRule, arg, ruleName) as IInstruction)!;
                        }

                        int count = 1;
                        if (context.expr(1) != null)
                        {
                            count = int.Parse(context.expr(1).GetText());
                        }

                        {
                            return new CallRule(variableIndex, ruleIndex, count);
                        }
                    }
                    case "each":
                    {
                        isScoped = true;
                        if (context.scope() == null)
                            AddError(ErrorCode.MissingEachScope, context, "Missing scope in foreach");
                        var exprs = context.expr();
                        var assignPick = new AssignPick(
                            variableIndex,
                            exprs.Length == 1
                                ? ParseExpr(exprs[0])!
                                : new And(exprs.Select(ParseExpr).Where(e => e != null).Cast<IValue>().ToList()),
                            CallType.Each,
                            ParseScope(context.scope()));
                        // _variables[variableIndex] = "";
                        {
                            return assignPick;
                        }
                    }
                    case "pick":
                    {
                        isScoped = false;
                        var exprs = context.expr();
                        {
                            return new AssignPick(
                                variableIndex,
                                exprs.Length == 1
                                    ? ParseExpr(exprs[0])!
                                    : new And(exprs.Select(ParseExpr).Where(e => e != null).Cast<IValue>().ToList()),
                                CallType.Pick);
                        }
                    }
                    case "create":
                        isScoped = false;
                        var type = context.expr(0).GetText().TrimQuotes();
                        var typeId = _database.GetEntityType(type);
                        if (typeId.Id == EntityTypeId.Null)
                        {
                            return ((IInstruction)AddError(ErrorCode.UnknownEntityType, context.expr(0), $"'{type}'"))!;
                        }

                        var name = ParseInterpolatedString(context.expr(1)?.GetText().TrimQuotes() ?? "");
                    {
                        return new CreateEntity(variableIndex, typeId.Id, name);
                    }
                    default:
                        isScoped = false;
                        return (AddError(ErrorCode.UnknownInstruction, context, funcName) as IInstruction)!;
                }
            }
        }

        private IInstruction[] ParseScope(MoiraiParser.ScopeContext scopeContext)
        {
            if (scopeContext == null)
                return Array.Empty<IInstruction>();
            return scopeContext.effect().Where(e => e.comment() == null).Select(ParseEffect).ToArray();
        }

        private InterpolatedString ParseInterpolatedString(string str)
        {
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
                var path = StoryParser.ParseExpr(this, pathStr, out _errors);
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

        private object AddError(ErrorCode code, ParserRuleContext loc, string msg)
        {
            Errors.Add(new Error(code, loc, msg));
            // to avoid warnings in a case where the parsing is already compromised
            return null!;
        }

        private object? AddError(ErrorCode code, ITerminalNode loc, string msg)
        {
            Errors.Add(new Error(code, loc, msg));
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
                        AddError(ErrorCode.VariableNotDeclared, varId, varId.GetText());
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
