using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

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
        UnknownEntityType
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
        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(ErrorCode.Lexer, line, charPositionInLine, "Lexer:" + msg));
        }
        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine,
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

    public static void SetupParser(string s, out Moirai parser, IVisitor visitor)
    {
        var lexer = new moirai_lexer(CharStreams.fromString(s /*.TrimStart('\r', '\n', ' ')*/));
        var tokens = new CommonTokenStream(lexer);
        parser = new Moirai(tokens);
        var listener = new Listener(visitor.Errors);
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);
    }

    public class AstVisitor : MoiraiBaseVisitor<object?>, IVisitor
    {
        private List<string> _variables = new();
        private List<Error> _errors = new();
        public List<Error> Errors => _errors;
        protected override object? DefaultResult => null;
        private readonly Database _database;
        public AstVisitor(Database database)
        {
            _database = database;
        }

        public override object? VisitType_definition(Moirai.Type_definitionContext context)
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

        public override object? VisitProp_definition(Moirai.Prop_definitionContext context)
        {
            var propName = context.ID(0).GetText();
            if (_database.GetProperty(propName).Id != 0)
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
        public override object? VisitEnum_definition(Moirai.Enum_definitionContext context)
        {
            EnumDefinition en = new((ushort)_database.Enums.Count, context.TYPE_ID(0).GetText(),
                context.TYPE_ID().Skip(1).Select(v => v.GetText()).ToList());
            _database.Enums.Add(en);
            return null;
        }

        public override object? VisitAction(Moirai.ActionContext context)
        {
            string actionId = context.ID().GetText();
            //Console.WriteLine("@ " + actionId);
            _variables.Clear();
            var action = new Action(actionId, false);
            foreach (Moirai.EffectContext effectContext in context.effect())
            {
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
        public override object? VisitEvent(Moirai.EventContext context)
        {
            string actionId = context.ID().GetText();
            //Console.WriteLine("@ " + actionId);
            var action = new Action(actionId, true);
            _variables.Clear();
            foreach (var whenContext in context.when())
            {
                action.Whens.Add(ParseWhen(whenContext));
            }
            _database.Events.Add(action);
            foreach (var effectContext in context.effect())
            {
                var effect = ParseEffect(effectContext);
                action.Effects.Add(effect);
            }
            return null;
        }
        private IInstruction ParseEffect(Moirai.EffectContext effectContext)
        {
            if (effectContext.call_assign() != null)
                return ParseCall(effectContext.call_assign());
            if (effectContext.var() != null)
                return ParseVar(effectContext.var());
            return ParseSet(effectContext.set());
        }
        private AssignPick ParseWhen(Moirai.WhenContext context)
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
        public override object? VisitWhen(Moirai.WhenContext context)
        {
            throw new System.NotImplementedException();

        }
        public override object? VisitSet(Moirai.SetContext context)
        {
            throw new System.NotImplementedException();

            return null;
        }
        private SetProperty ParseVar(Moirai.VarContext context)
        {
            var name = context.VAR_ID();
            DeclareVar(name.GetText(), name.Symbol, out var varIndex);
            PropertyValue.ValueType type = context.COLON() != null ? ParseType(context.ID() ?? context.TYPE_ID()) : default;
            var expr = ParseExpr(context.expr());
            return new SetProperty(new PropertyPath(varIndex), expr, true, type);
        }
        private SetProperty ParseSet(Moirai.SetContext context)
        {
            var left = ParsePath(context.path());
            var right = ParseExpr(context.expr()); //, left.Property);
            return new SetProperty(left, right, false, default);
        }
        private IValue ParseValue(Moirai.ValueContext value)
        {
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
                        if (Enum.TryParse(arg.GetText(), true, out RandomName.NameType nt))
                            return new RandomName(nt);
                        if (!_database.GetEnumDefinition(arg.GetText(), out var enumDef))
                            return (AddError(ErrorCode.UnknownEnum, arg, arg.GetText()) as IValue)!;

                        return new RandomCall(enumDef.Index);
                    }
                    case "passed_years":
                    {
                        // TODO error checking
                        int years = int.Parse(call.expr(0).GetText());
                        return new YearsPassed(years);
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
        // private ComputedValue ParseComputedValue(Moirai.ValueContext value, PropertyId type)
        // {
        //     if(!type.IsValid || !_database.GetPropertyType(type, out PropertyValue.ValueType valueType))
        //     {
        //         throw new Exception("Property has no type");
        //     }
        //     
        //     // TODO use proper EntityType type
        //     if (type == Database.PropType)
        //     {
        //         var typeId = _database.GetEntityType(value.@string().GetString());
        //             
        //         if (typeId == 0)
        //            AddError( ErrorCode.UnknownPropertyType,  value, value.@string().GetText());
        //         return new ComputedValue(typeId);
        //     }
        //     // TODO only call in value
        //     if (value.call()?.ID()?.GetText() == "random")
        //     {
        //         if(valueType.BaseType == PropertyValue.ValueBaseType.Enum)
        //         {
        //             var enumDef = _database.Enums[valueType.Index];
        //             return new ComputedValue(enumDef);
        //         }
        //         AddError(ErrorCode.UnknownCall, value, "Random only supported for enums");
        //         return default;
        //         // switch (valueType.BaseType)
        //         // {
        //         //     case PropertyValue.ValueBaseType.Enum
        //         // }
        //     }
        //     if (value.path() != null)
        //     {
        //         PropertyPath path = ParsePath(value.path());
        //         return new ComputedValue(path);
        //     }
        //     switch (valueType.BaseType)
        //     {
        //         case PropertyValue.ValueBaseType.String:
        //             return new ComputedValue(value.@string().STRING().GetText());
        //         case PropertyValue.ValueBaseType.Ref:
        //             if (value.NULL() != null)
        //                 return new ComputedValue((PropertyValue)EntityId.Null);
        //             throw new System.NotImplementedException("Literal ref not supported");
        //         case PropertyValue.ValueBaseType.Number:
        //             return (ComputedValue)(PropertyValue)int.Parse(value.number().GetText());
        //         case PropertyValue.ValueBaseType.Bool:
        //             return (ComputedValue)(PropertyValue)(value.@bool().GetText() == "true");
        //         case PropertyValue.ValueBaseType.Enum:
        //             if (value.@string() != null)
        //             {
        //                 if(!_database.Enums[valueType.Index].GetValueFromName(value.@string().GetString(), out PropertyValue v))
        //                     AddError(ErrorCode.UnknownEnumValue,  value, $"'{value.@string().GetString()}' in enum {_database.Enums[valueType.Index].Name}");
        //
        //                 return new ComputedValue(v);
        //             }
        //             throw new System.NotImplementedException();
        //         case PropertyValue.ValueBaseType.EntityType:
        //             throw new System.NotImplementedException();
        //         case PropertyValue.ValueBaseType.None:
        //         default:
        //             throw new ArgumentOutOfRangeException();
        //     }
        // }
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
        private IInstruction ParseCall(Moirai.Call_assignContext context)
        {
            var funcName = context.ID().GetText();
            if (funcName == "assert")
            {
                return new AssertInstr(ParseExpr(context.expr(0)), context.expr(0).GetText());
            }
            if (funcName == "assert_eq")
            {
                return new AssertInstr(ParseExpr(context.expr(0)), ParseExpr(context.expr(1)),
                    context.expr(0).GetText() + " = " + context.expr(1).GetText());
            }
            int variableIndex = Math.Max(0, _variables.Count - 1);
            if (context.VAR_ID() != null)
            {
                if (!DeclareVar(context.VAR_ID().GetText(), context.Start, out variableIndex))
                    return null;
            }
            switch (funcName)
            {
                case "call":
                {
                    var arg = context.expr(0);
                    string? ruleName = arg.value()?.path()?.GetText() ?? arg.value()?.@string()?.GetString();
                    if (ruleName == null)
                        return (AddError(ErrorCode.MissingArgument, context, "rule name") as IInstruction)!;
                    var ruleIndex = _database.Actions.FindIndex(r => r.Name == ruleName);
                    if (ruleIndex == -1)
                        return (AddError(ErrorCode.UnknownRule, arg, ruleName) as IInstruction)!;
                    return new CallRule(variableIndex, ruleIndex);
                }
                case "each":
                {
                    if (context.scope() == null)
                        AddError(ErrorCode.MissingEachScope, context, "Missing scope in foreach");
                    var exprs = context.expr();
                    var assignPick = new AssignPick(
                        variableIndex,
                        exprs.Length == 1
                            ? ParseExpr(exprs[0])!
                            : new And(exprs.Select(ParseExpr).Where(e => e != null).Cast<IValue>().ToList()),
                        CallType.Each,
                        context.scope().effect().Select(ParseEffect).ToArray());
                    // _variables[variableIndex] = "";
                    return assignPick;
                }
                case "pick":
                {
                    var exprs = context.expr();
                    return new AssignPick(
                        variableIndex,
                        exprs.Length == 1
                            ? ParseExpr(exprs[0])!
                            : new And(exprs.Select(ParseExpr).Where(e => e != null).Cast<IValue>().ToList()),
                        CallType.Pick);
                }
                case "create":
                    var type = context.expr(0).GetText().TrimQuotes();
                    var typeId = _database.GetEntityType(type);
                    if (typeId.Id == EntityTypeId.Null)
                        return ((IInstruction)AddError(ErrorCode.UnknownEntityType,  context.expr(0), $"'{type}'"))!;

                    var name = ParseInterpolatedString(context.expr(1)?.GetText().TrimQuotes() ?? "");
                    return new CreateEntity(variableIndex, typeId.Id, name);
                case "record":
                    var stringContext = context.expr(0).value().@string();
                    var interpolatedString = ParseInterpolatedString(stringContext.GetString());
                    return new FormatAction(interpolatedString);
            }

            return (AddError(ErrorCode.UnknownInstruction, context, funcName) as IInstruction)!;
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
                    throw new System.NotImplementedException($"Missing curly brace in string: {str}, opening brace at {i}");

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
        public IValue? ParseExpr(Moirai.ExprContext context)
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
        // private object? AddError(ErrorCode code, IToken loc, string msg)
        // {
        //     Errors.Add(new Error(code, loc, msg));
        //     return null;
        // }
        private object? AddError(ErrorCode code, ParserRuleContext loc, string msg)
        {
            Errors.Add(new Error(code, loc, msg));
            return null;
        }
        private object? AddError(ErrorCode code, ITerminalNode loc, string msg)
        {
            Errors.Add(new Error(code, loc, msg));
            return null;
        }
        public override object? VisitCall(Moirai.CallContext context)
        {
            throw new System.NotImplementedException();
        }
        public override object? VisitExpr(Moirai.ExprContext context)
        {
            throw new System.NotImplementedException();
        }

        public override object? VisitPath(Moirai.PathContext context)
        {
            throw new System.NotImplementedException();
        }


        public PropertyPath ParsePath(Moirai.PathContext context)
        {
            

            if (context.ID().Length > 1)
                throw new Exception("expected two parts, got " + context.ID().Length);
            
            var propertyId = PropertyId.Null;
            if (context.ID(0) != null)
            {
                var propertyName = context.ID(0)?.GetText();
                propertyId = _database.GetProperty(propertyName.ToLowerInvariant());
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
            int variableIndex = Math.Max(0, _variables.Count - 1);
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
    public static string GetString(this Moirai.StringContext context) => context.STRING().GetText().TrimQuotes();
}