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
        TypeNameMustStartWithUpperCase
    }
    public struct Error
    {
        public readonly ErrorCode Code;
        public int Line, Col;
        public string Message;
        public Error(ErrorCode code, int line, int col, string message)
        {
            Code = code;
            Line = line;
            Col = col;
            Message = message;
        }
        public Error(ErrorCode code, IToken loc, string message)
        {
            Code = code;
            Line = loc.Line;
            Col = loc.Column;
            Message = message;
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

    internal static PropertyPath ParsePath(Visitor visitor, string s, out List<Error> errors)
    {
        SetupParser(s, out errors, out var parser, visitor);
        var r = parser.path();
        return visitor.ParsePath(r);

    }

    public static Database Parse(string s, out List<Error> errors)
    {
        var db = new Database();
        var visitor = new Visitor(db);
        SetupParser(s, out errors, out var parser, visitor);
        var r = parser.r();
        r.Accept(visitor);
        return db;
    }

    public static void SetupParser(string s, out List<Error> errors, out Moirai parser, IVisitor visitor)
    {

        errors = visitor.Errors;
        var lexer = new moirai_lexer(CharStreams.fromString(s /*.TrimStart('\r', '\n', ' ')*/));
        var tokens = new CommonTokenStream(lexer);
        parser = new Moirai(tokens);
        var listener = new Listener(errors);
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);
    }

    internal class Visitor : MoiraiBaseVisitor<object?>, IVisitor
    {
        private List<string> _variables = new();
        private List<Error> _errors = new();
        public List<Error> Errors => _errors;
        protected override object? DefaultResult => null;
        private readonly Database _database;
        public Visitor(Database database)
        {
            _database = database;
        }

        public override object? VisitType_definition(Moirai.Type_definitionContext context)
        {
            if (context.TYPE_ID() == null)
                return AddError(ErrorCode.TypeNameMustStartWithUpperCase, context.Start, context.GetText());
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
                return AddError(ErrorCode.DuplicatePropertyDefinition,  context.Start, propName);

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
                    AddError(ErrorCode.UnknownPropertyType,  id.Symbol, id.GetText());
                    return default;
            }
        }
        public override object? VisitEnum_definition(Moirai.Enum_definitionContext context)
        {
            EnumDefinition en = new((ushort)_database.Enums.Count, context.TYPE_ID(0).GetText(), context.TYPE_ID().Skip(1).Select(v => v.GetText()).ToList());
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
        private SetProperty ParseSet(Moirai.SetContext context)
        {
            var left = ParsePath(context.path());
            var right = ParseExpr(context.expr());//, left.Property);
            return new SetProperty(left, right);
        }
        private IValue ParseValue(Moirai.ValueContext value)
        {
            if (value.expr() != null)
                return ParseExpr(value.expr());
            if (value.TYPE_ID() != null)
            {
                var type = _database.GetEntityType(value.TYPE_ID().GetText());
                if(!type.Id.IsValid)
                    AddError(ErrorCode.UnknownPropertyType, value.Start, value.TYPE_ID().GetText());
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
                        if (!_database.GetEnumDefinition(call.expr(0).GetText(), out var enumDef))
                            return (AddError(ErrorCode.UnknownEnum, call.expr(0).Start, call.expr(0).GetText()) as IValue)!;
                        return new RandomCall(enumDef.Index);
                    }
                    case "passed_years":
                    {
                        // TODO error checking
                        int years = int.Parse(call.expr(0).GetText());
                        return new YearsPassed(years);
                    }
                    default:
                        AddError(ErrorCode.UnknownCall, value.Start, funcName);
                        return default!;
                }
            }
            if (value.path() != null)
            {
                PropertyPath path = ParsePath(value.path());
                return path;
            }
            
            if(value.@string() != null)
                return new Literal(value.@string().STRING().GetText());
            if (value.NULL() != null)
                return new Literal(EntityId.Null);
            if(value.number() != null)
                return new Literal(int.Parse(value.number().GetText()));
            if(value.@bool() != null)
                return new Literal(value.@bool().TRUE() != null);

            if (value.enum_value() != null)
            {
                var enumType = value.enum_value().TYPE_ID(0);
                if (!_database.GetEnumDefinition(enumType.GetText(), out var enumDef))
                    return (AddError(ErrorCode.UnknownEnum, enumType.Symbol, enumType.GetText()) as IValue)!;

                var enumValue = value.enum_value().TYPE_ID(1);
                if (!enumDef.GetValueFromName(enumValue.GetText(), out var val))
                    return (AddError(ErrorCode.UnknownEnumValue, enumValue.Symbol, enumValue.GetText() + " in enum " + enumDef.Name) as IValue)!;

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
        //            AddError( ErrorCode.UnknownPropertyType,  value.Start, value.@string().GetText());
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
        //         AddError(ErrorCode.UnknownCall, value.Start, "Random only supported for enums");
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
        //                     AddError(ErrorCode.UnknownEnumValue,  value.Start, $"'{value.@string().GetString()}' in enum {_database.Enums[valueType.Index].Name}");
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

            if (_variables.IndexOf(variable) != -1)
            {
                AddError(ErrorCode.DuplicateVariableDefinition,  contextStart, " Duplicate variable " + variable);
                varIndex = 0;
                return false;
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
            int variableIndex = Math.Max(0,  _variables.Count - 1);
            if (context.VAR_ID() != null)
            {
                if (!DeclareVar(context.VAR_ID().GetText(), context.Start, out variableIndex))
                    return null;
            }
            switch (funcName)
            {

                case "each":
                {
                    if (context.scope() == null)
                        AddError(ErrorCode.MissingEachScope, context.Start, "Missing scope in foreach");
                    var exprs = context.expr();
                    return new AssignPick(
                        variableIndex,
                        exprs.Length == 1
                            ? ParseExpr(exprs[0])!
                            : new And(exprs.Select(ParseExpr).Where(e => e != null).Cast<IValue>().ToList()),
                        CallType.Each,
                        context.scope().effect().Select(ParseEffect).ToArray());
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
                        throw new Exception($"Unknown entity type '{type}'");
                    return new CreateEntity(variableIndex, typeId.Id);
                case "format":
                    var str = context.expr(0).value(0).@string().GetString();
                    List<PropertyPath> paths = new();
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
                        var path = StoryParser.ParsePath(this, pathStr, out _errors);
                        paths.Add(path);
                        // Console.WriteLine($"'{pathStr}'");
                        if (i > prev)
                            result += str.Substring(prev, i - prev);
                        result += $"{{{paths.Count - 1}}}";
                        i = j + 1;
                        prev = i;
                    }
                    if (prev < str.Length)
                        result += (str.Substring(prev));
                    // Console.WriteLine($"res:'{result}'");
                    return new FormatAction(result, paths.ToArray());
            }

            return (AddError(ErrorCode.UnknownCall, context.Start, funcName) as IInstruction)!;
        }
        private IValue? ParseExpr(Moirai.ExprContext context)
        {
            if (context.op() == null)
            {
                return ParseValue(context.value(0));
                // ComputedValue v = ParseValue(context.value(0), PropertyValue.TypeBool);

            }
            string? op = context.op().GetText();
            // left, alive
            var left = context.value(0);
            var leftPath = ParseValue(left);

            // right, true or $x -  not alive or $x.alive
            IValue rightValue = ParseValue(context.value(1));

            BinaryOperator.Operator pop;
            switch (op)
            {
                case "=":
                    pop = BinaryOperator.Operator.Equals;
                    break;
                case "!=":
                    pop = BinaryOperator.Operator.NotEquals;
                    break;
                default: return (IValue?)AddError(ErrorCode.UnknownExpressionOperator,  context.Start, op);
            }
            return new BinaryOperator(pop, leftPath, rightValue);
        }
        private object? AddError(ErrorCode code, IToken loc, string msg)
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
            int variableIndex = Math.Max(0,  _variables.Count - 1);
            var varId = context.VAR_ID();
            if (varId != null)
            {
                if (!int.TryParse(varId.GetText().Substring(1), out variableIndex))
                {

                    variableIndex = _variables.IndexOf(varId.GetText());
                    if (variableIndex == -1)
                        throw new System.NotImplementedException("Unknown var " + varId.GetText());
                }
            }
            if (context.ID().Length == 0)
                return new PropertyPath(variableIndex);

            if (context.ID().Length > 1)
                throw new Exception("expected two parts, got " + context.ID().Length);

            var propertyName = context.ID(0).GetText();
            var indexOf = _database.GetProperty(propertyName.ToLowerInvariant());
            if (!indexOf.IsValid)
            {
                AddError(ErrorCode.UnknownProperty,  context.ID(0).Symbol, propertyName);
                return default;
            }
            return new PropertyPath(variableIndex, indexOf);
        }
    }
}

internal static class ParsingExtensions
{
    public static string TrimQuotes(this string s) => s.Trim('"', '\'');
    public static string GetString(this Moirai.StringContext context) => context.STRING().GetText().TrimQuotes();
}