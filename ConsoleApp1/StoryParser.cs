using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

public static class StoryParser
{
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

    public static List<Action> Parse(string s, out List<Error> errors)
    {
        var visitor = new Visitor();
        errors = visitor.Errors;
        var lexer = new storygenLexer(CharStreams.fromString(s /*.TrimStart('\r', '\n', ' ')*/));
        var tokens = new CommonTokenStream(lexer);
        var parser = new storygenParser(tokens);
        var listener = new Listener(errors);
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);
        var r = parser.r();
        r.Accept(visitor);
        return visitor.Actions;
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
        public override string ToString() => $"{Line}{Col}: {Message}";
    }

    class Visitor : storygenBaseVisitor<object?>
    {
        public List<Action> Actions = new();
        private List<string> _variables = new();
        public List<Error> Errors = new();
        protected override object? DefaultResult => null;

        public override object? VisitAction(storygenParser.ActionContext context)
        {
            string actionId = context.ACTION_ID().GetText().Substring(1);
            //Console.WriteLine("@ " + actionId);
            Actions.Add(new Action(actionId));
            _variables.Clear();
            return base.VisitAction(context);
        }
        public override object? VisitSet(storygenParser.SetContext context)
        {
            //Console.WriteLine($"  Set {context.path().GetText()} = {context.value().GetText()}");
            var left = ParsePath(context.path());
            var right = ParseComputedValue(context.value(), left.Property);
            Actions.Last().Effects.Add(new SetProperty(left, right));
            return null;
        }
        private ComputedValue ParseComputedValue(storygenParser.ValueContext value, PropertyType? type)
        {
            ComputedValue pp;
            if (value.NULL() != null)
                pp = new ComputedValue((PropertyValue)EntityId.Null);
            else if (value.@string() != null)
            {
                if (type == PropertyType.Type)
                {
                    if (Enum.TryParse<EntityType>(value.@string().STRING().GetText().Trim('"'), true, out var entityType))
                    {
                        pp = new ComputedValue((int)entityType);
                    }
                    else throw new System.NotImplementedException("unknown type " + value.@string().GetText());
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
        // public override object? VisitCreate(storygenParser.CreateContext context)
        // {
        //     var type = context.@string().GetText().Trim('"');
        //     //Console.WriteLine("  Create " + type);
        //     Actions.Last().Effects.Add(new CreateEntity(Enum.Parse<EntityType>(type, true)));
        //     return base.VisitCreate(context);
        // }
        public override object? VisitAssign(storygenParser.AssignContext context)
        {
            var variable = context.VAR_ID().GetText();
            if (_variables.IndexOf(variable) != -1)
                return AddError(context.Start, " Duplicate variable " + variable);

            _variables.Add(variable);

            IEffect callEffect = ParseCall(context.call(), _variables.Count - 1);

            Actions.Last().Effects.Add(callEffect);
            //Console.WriteLine("  Assign " + context.VAR_ID());
            return null;
        }
        private IEffect ParseCall(storygenParser.CallContext context, int variableIndex)
        {
            var funcName = context.ID().GetText();
            switch (funcName)
            {
                case "pick":
                    var exprs = context.expr();
                    return new AssignPick(
                        variableIndex,
                        exprs.Length == 1 ? ParseExpr(exprs[0]) : new And(exprs.Select(ParseExpr).ToList()));
                case "create":
                    var type = context.expr(0).GetText().Trim('"');
                    return new CreateEntity(variableIndex, Enum.Parse<EntityType>(type, true));
            }

            throw new NotImplementedException($"Unknown call '{funcName}'");
        }
        private IPredicate? ParseExpr(storygenParser.ExprContext context)
        {
            string? op = context.op().GetText();
            // left, alive
            var left = context.value(0);
            ComputedValue leftValue = ParseComputedValue(left, null);
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
            return new PropertyOperator(pop, leftValue.Path.Property.Value, rightValue);
        }
        private object? AddError(IToken loc, string msg)
        {
            Errors.Add(new Error(loc, msg));
            return null;
        }
        public override object? VisitCall(storygenParser.CallContext context)
        {

            Actions.Last().Effects.Add(ParseCall(context, _variables.Count));
            return null;
        }
        public override object? VisitExpr(storygenParser.ExprContext context)
        {
            throw new System.NotImplementedException();
        }

        public override object? VisitPath(storygenParser.PathContext context)
        {
            throw new System.NotImplementedException();
        }

        private static PropertyType ParsePropertyType(ITerminalNode id) => Enum.Parse<PropertyType>(id.GetText(), true);

        PropertyPath ParsePath(storygenParser.PathContext context)
        {
            int varIndex = 0;
            var varId = context.VAR_ID();
            if (varId != null)
            {
                varIndex = _variables.IndexOf(varId.GetText());
                if (varIndex == -1)
                    throw new System.NotImplementedException("Unknown var " + varId.GetText());

            }
            if (context.ID().Length == 0)
                return new PropertyPath(varIndex);

            if (context.ID().Length > 1)
                throw new Exception("expected two parts, got " + context.ID().Length);

            PropertyType type = Enum.Parse<PropertyType>(context.ID(0).GetText(), true);
            return new PropertyPath(varIndex, type);
        }
    }
}