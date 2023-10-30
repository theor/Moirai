using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

public static class StoryParser
{
    class Listener: IAntlrErrorListener<int>,IAntlrErrorListener<IToken>
    {
        private readonly List<Error> _errors;
        public Listener(List<Error> errors)
        {
            _errors = errors;

        }
        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(line, charPositionInLine, "Lexer:" + msg));
        }
        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(line, charPositionInLine,"Parser:" +  msg));
        }
    }

    public static List<Action> Parse(string s, out List<Error> errors)
    {
        var lexer = new storygenLexer(CharStreams.fromString(s /*.TrimStart('\r', '\n', ' ')*/));
        var tokens = new CommonTokenStream(lexer);
        var parser = new storygenParser(tokens);
        errors = new();
        var listener = new Listener(errors);
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);
        var r = parser.r();
        var visitor = new Visitor();
        errors.AddRange(r.Accept(visitor));
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

    class Visitor : storygenBaseVisitor<List<Error>>
    {
        public List<Action> Actions = new();
        private List<string> _variables = new();
        private List<PropertyValue> _values = new();
        private List<IPredicate> _predicates = new();
        protected override List<Error> DefaultResult => new();
        protected override List<Error> AggregateResult(List<Error> aggregate, List<Error>? nextResult)
        {
            if (nextResult == null)
                return aggregate;

            aggregate.AddRange(nextResult);
            return aggregate;
        }
        public override List<Error> VisitAction(storygenParser.ActionContext context)
        {
            string actionId = context.ACTION_ID().GetText().Substring(1);
            //Console.WriteLine("@ " + actionId);
            Actions.Add(new Action(actionId));
            _variables.Clear();
            return base.VisitAction(context);
        }
        public override List<Error> VisitSet(storygenParser.SetContext context)
        {
            _values.Clear();
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
                pp = new ComputedValue((PropertyValue)0);
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
            else
                throw new System.NotImplementedException(value.GetText());

            return pp;
        }
        // public override List<Error> VisitCreate(storygenParser.CreateContext context)
        // {
        //     var type = context.@string().GetText().Trim('"');
        //     //Console.WriteLine("  Create " + type);
        //     Actions.Last().Effects.Add(new CreateEntity(Enum.Parse<EntityType>(type, true)));
        //     return base.VisitCreate(context);
        // }
        public override List<Error> VisitAssign(storygenParser.AssignContext context)
        {
            _predicates.Clear();
            var variable = context.VAR_ID().GetText();
            if (_variables.IndexOf(variable) != -1)
                return new List<Error>() { new Error(context.Start.Column, context.Start.Line, " Duplicate variable " + variable) };

            _variables.Add(variable);
            //Console.WriteLine("  Assign " + context.VAR_ID());
            var visitAssign = base.VisitAssign(context);
            Actions.Last().Effects.Add(new Assign(
                _variables.Count - 1,
                _predicates.Count == 1 ? _predicates[0] : new And(_predicates)));
            return visitAssign;
        }
        public override List<Error> VisitCall(storygenParser.CallContext context)
        {
            var funcName = context.ID().GetText();
            //Console.WriteLine("    Call " + funcName);
            if (funcName == "create")
            {
                var type = context.expr(0).GetText().Trim('"');
                //     //Console.WriteLine("  Create " + type);
                Actions.Last().Effects.Add(new CreateEntity(Enum.Parse<EntityType>(type, true)));
                return null;
            }
            if (funcName != "pick")
                return new() { new Error(context.Start, "call unknown: " + funcName) };
            return base.VisitCall(context);
        }
        public override List<Error> VisitExpr(storygenParser.ExprContext context)
        {
            _values.Clear();
            //Console.WriteLine("    VisitExpr " + context.GetText());
            // Actions.Last().Effects.
            // if(_values.Count != 1)
            // throw new System.NotImplementedException($"Value count: {_values.Count} != 1");

            IPredicate predicate = null;
            string? op = context.op().GetText();
            // left, alive
            var left = context.value(0);
            ComputedValue leftValue = ParseComputedValue(left, null);
            if(leftValue.Type != ComputedValue.ComputedValueType.Path)
                throw new System.NotImplementedException();
            // right, true or $x -  not alive or $x.alive
            ComputedValue rightValue = ParseComputedValue(context.value(1), leftValue.Path.Property);
            if (op == "=")
                predicate = new PropertyEquals(leftValue.Path.Property.Value, rightValue);
            else if (op == "!=")
                predicate = new PropertyNotEquals(leftValue.Path.Property.Value, rightValue);
            else
                return new List<Error> { new Error(context.Start, "Unknown Expr op: " + op) };

            _predicates.Add(predicate);
            return null;
        }

        public override List<Error> VisitPath(storygenParser.PathContext context)
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
                throw new Exception("expected two parts, got " + context.ID().Length );

            PropertyType type = Enum.Parse<PropertyType>(context.ID(0).GetText(), true);
            return new PropertyPath(varIndex, type);
        }
    }
}