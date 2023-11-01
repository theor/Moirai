using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

public static class StoryParser
{
    public interface IVisitor
    {
        List<Error> Errors { get; }
    }

    public struct Error
    {
        public int Line, Col;
        public string Message;
        public Error(int line, int col, string message)
        {
            Line = line;
            Col = col;
            Message = message;
        }
        public Error(IToken loc, string message)
        {
            Line = loc.Line;
            Col = loc.Column;
            Message = message;
        }
        public override string ToString() => $"{Line}:{Col}: {Message}";
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
            _errors.Add(new Error(line, charPositionInLine, "Lexer:" + msg));
        }
        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(line, charPositionInLine, "Parser:" + msg));
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
            var typeName = context.ID().GetText();
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
            if (_database.Properties.IndexOf(propName) != -1)
                return AddError(context.Start, $"Multiple definitions of the property '{propName}'");

            _database.Properties.Add(propName);
            return null;
        }
        public override object? VisitEnum_definition(Moirai.Enum_definitionContext context)
        {
            EnumDefinition en = new(context.ID(0).GetText(), context.ID().Skip(1).Select(v => v.GetText()).ToList());
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
        private IEffect ParseEffect(Moirai.EffectContext effectContext)
        {
            if (effectContext.call() != null)
                return ParseCall(effectContext.call());

            return ParseSet(effectContext.set());
        }
        private AssignPick ParseWhen(Moirai.WhenContext context)
        {
            var exprs = context.expr();
            var predicate = exprs.Length == 1
                ? ParseExpr(exprs[0])!
                : new And(exprs.Select(ParseExpr).Where(e => e != null).Cast<IPredicate>().ToList());
            var variableIndex = 0;
            if (context.VAR_ID() != null)
            {
                if (!DeclareVar(context.VAR_ID().GetText(), context.VAR_ID().Symbol, out variableIndex))
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
            //Console.WriteLine($"  Set {context.path().GetText()} = {context.value().GetText()}");

            return null;
        }
        private SetProperty ParseSet(Moirai.SetContext context)
        {
            var left = ParsePath(context.path());
            var right = ParseComputedValue(context.value(), left.Property);
            return new SetProperty(left, right);
        }
        private ComputedValue ParseComputedValue(Moirai.ValueContext value, PropertyId type)
        {
            ComputedValue pp;
            if (value.NULL() != null)
                pp = new ComputedValue((PropertyValue)EntityId.Null);
            else if (value.@string() != null)
            {
                if (type == Database.PropType)
                {
                    var typeId = _database.GetEntityType(value.@string().GetString());
                    
                    if (typeId == 0)
                        throw new System.NotImplementedException("unknown type " + value.@string().GetText());
                    pp = new ComputedValue(typeId);
                }
                else
                    pp = new ComputedValue(value.@string().STRING().GetText());
            }
            else if (value.path() != null)
            {
                PropertyPath path = ParsePath(value.path());
                pp = new ComputedValue(path);


            }
            else if (value.@bool() != null)
                pp = (ComputedValue)(PropertyValue)(value.@bool().GetText() == "true");
            else if (value.number() != null)
                pp = (ComputedValue)(PropertyValue)(int.Parse(value.number().GetText()));
            else
                throw new System.NotImplementedException(value.GetText());

            return pp;
        }
        private bool DeclareVar(string variable, IToken contextStart, out int varIndex)
        {

            if (_variables.IndexOf(variable) != -1)
            {
                AddError(contextStart, " Duplicate variable " + variable);
                varIndex = 0;
                return false;
            }

            _variables.Add(variable);
            varIndex = _variables.Count - 1;
            return true;
        }
        private IEffect ParseCall(Moirai.CallContext context)
        {
            int variableIndex = _variables.Count;
            if (context.VAR_ID() != null)
            {
                if (!DeclareVar(context.VAR_ID().GetText(), context.Start, out variableIndex))
                    return null;
            }
            var funcName = context.ID().GetText();
            switch (funcName)
            {

                case "each":
                {
                    if (context.scope() == null)
                        AddError(context.Start, "Missing scope in foreach");
                    var scopeEffects = context.scope().effect().Select(ParseEffect).ToArray();
                    var exprs = context.expr();
                    return new AssignPick(
                        variableIndex,
                        exprs.Length == 1
                            ? ParseExpr(exprs[0])!
                            : new And(exprs.Select(ParseExpr).Where(e => e != null).Cast<IPredicate>().ToList()),
                        CallType.Each,
                        scopeEffects);
                }
                case "pick":
                {
                    var exprs = context.expr();
                    return new AssignPick(
                        variableIndex,
                        exprs.Length == 1
                            ? ParseExpr(exprs[0])!
                            : new And(exprs.Select(ParseExpr).Where(e => e != null).Cast<IPredicate>().ToList()),
                        CallType.Pick);
                }
                case "create":
                    var type = context.expr(0).GetText().TrimQuotes();
                    var typeId = _database.GetEntityType(type);
                    if (typeId == 0)
                        throw new Exception($"Unknown entity type '{type}'");
                    return new CreateEntity(variableIndex, typeId);
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

            throw new NotImplementedException($"Unknown call '{funcName}'");
        }
        private IPredicate? ParseExpr(Moirai.ExprContext context)
        {
            string? op = context.op().GetText();
            // left, alive
            var left = context.value(0);
            ComputedValue leftValue = ParseComputedValue(left, PropertyId.Null);
            if (leftValue.Type != ComputedValue.ComputedValueType.Path)
                throw new System.NotImplementedException();

            // right, true or $x -  not alive or $x.alive
            ComputedValue rightValue = ParseComputedValue(context.value(1), leftValue.Path.Property);

            PropertyOperator.Operator pop;
            switch (op)
            {
                case "=":
                    pop = PropertyOperator.Operator.Equals;
                    break;
                case "!=":
                    pop = PropertyOperator.Operator.NotEquals;
                    break;
                default: return (IPredicate?)AddError(context.Start, "Unknown Expr op: " + op);
            }
            return new PropertyOperator(pop, leftValue.Path.Property, rightValue);
        }
        private object? AddError(IToken loc, string msg)
        {
            Errors.Add(new Error(loc, msg));
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
            int varIndex = 0;
            var varId = context.VAR_ID();
            if (varId != null)
            {
                if (!int.TryParse(varId.GetText().Substring(1), out varIndex))
                {

                    varIndex = _variables.IndexOf(varId.GetText());
                    if (varIndex == -1)
                        throw new System.NotImplementedException("Unknown var " + varId.GetText());
                }
            }
            if (context.ID().Length == 0)
                return new PropertyPath(varIndex);

            if (context.ID().Length > 1)
                throw new Exception("expected two parts, got " + context.ID().Length);

            var propertyName = context.ID(0).GetText();
            var indexOf = _database.Properties.IndexOf(propertyName.ToLowerInvariant());
            if (indexOf == -1)
            {
                AddError(context.ID(0).Symbol, $"Unknown property '{propertyName}'");
                return default;
            }
            PropertyId pid = new PropertyId((uint)indexOf);

            return new PropertyPath(varIndex, pid);
        }
    }
}

internal static class ParsingExtensions
{
    public static string TrimQuotes(this string s) => s.Trim('"', '\'');
    public static string GetString(this Moirai.StringContext context) => context.STRING().GetText().TrimQuotes();
}