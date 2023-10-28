using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

public static class StoryParser
{
    public static List<Action> Parse(string s, out List<Error> errors)
    {
        var lexer = new storygenLexer(CharStreams.fromString(s /*.TrimStart('\r', '\n', ' ')*/));
        var tokens = new CommonTokenStream(lexer);
        var parser = new storygenParser(tokens);

        lexer.AddErrorListener(ConsoleErrorListener<int>.Instance);
        parser.AddErrorListener(ConsoleErrorListener<IToken>.Instance);
        var r = parser.r();
        var visitor = new Visitor();
        errors = r.Accept(visitor);
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
            Console.WriteLine("@ " + actionId);
            Actions.Add(new Action(actionId));
            _variables.Clear();
            return base.VisitAction(context);
        }
        public override List<Error> VisitSet(storygenParser.SetContext context)
        {
            _values.Clear();
            Console.WriteLine($"  Set {context.path().GetText()} = {context.value().GetText()}");
            var left = ParsePath(context.path());
            var right = ParsePredicateParameter(context.value(), left.prop);
            Actions.Last().Effects.Add(new SetProperty(left.varIndex, left.prop, right));
            return null;
        }
        private PredicateParameter ParsePredicateParameter(storygenParser.ValueContext value, PropertyType type)
        {
            PredicateParameter pp;
            if (value.NULL() != null)
                pp = new PredicateParameter(0);
            else if (value.path() != null)
            {
                string? varName = value.path().VAR_ID()?.GetText();
                if (varName != null)
                {
                    int varIdx = _variables.IndexOf(varName);
                    if (varIdx == -1)
                        throw new System.NotImplementedException("Unknown var " + varName);

                    pp = PredicateParameter.Argument(varIdx);
                }
                else if (type == PropertyType.Type)
                {
                    if (Enum.TryParse<EntityType>(value.path().GetText(), true, out var entityType))
                    {
                        pp = new PredicateParameter((int)entityType);
                    }
                    else throw new System.NotImplementedException("unknown type" + value.path().GetText());
                }
                else
                    throw new System.NotImplementedException("not a bool or var: " + value.path().ID(0));
            }
            else
                pp = (PropertyValue)(value.@bool().GetText() == "true");
            return pp;
        }
        public override List<Error> VisitCreate(storygenParser.CreateContext context)
        {
            var type = context.ID().GetText();
            Console.WriteLine("  Create " + type);
            Actions.Last().Effects.Add(new CreateEntity(Enum.Parse<EntityType>(type, true)));
            return base.VisitCreate(context);
        }
        public override List<Error> VisitAssign(storygenParser.AssignContext context)
        {
            _predicates.Clear();
            var variable = context.VAR_ID().GetText();
            if (_variables.IndexOf(variable) != -1)
                return new List<Error>() { new Error(context.Start.Column, context.Start.Line, " Duplicate variable " + variable) };

            _variables.Add(variable);
            Console.WriteLine("  Assign " + context.VAR_ID());
            var visitAssign = base.VisitAssign(context);
            Actions.Last().Effects.Add(new PredicateParameter(
                 _predicates.Count == 1 ? _predicates[0] : 
                    new And(_predicates)){ArgumentIndex = _variables.Count - 1});
            return visitAssign;
        }
        public override List<Error> VisitCall(storygenParser.CallContext context)
        {
            var funcName = context.ID().GetText();
            Console.WriteLine("    Call " + funcName);
            if (funcName != "pick")
                return new() { new Error(context.Start, "call unknown: " + funcName) };

            return base.VisitCall(context);
        }
        public override List<Error> VisitExpr(storygenParser.ExprContext context)
        {
            _values.Clear();
            Console.WriteLine("    VisitExpr " + context.GetText());
            // Actions.Last().Effects.
            // if(_values.Count != 1)
            // throw new System.NotImplementedException($"Value count: {_values.Count} != 1");

            IPredicate predicate = null;
            string? op = context.op().GetText();
            // left, alive
            ITerminalNode? id = context.ID();
            PropertyType propertyType = ParsePropertyType(id);
            // right, true or $x -  not alive or $x.alive
            PredicateParameter value = ParsePredicateParameter(context.value(), propertyType);
            if (op == "=")
                predicate = new PropertyEquals(propertyType, value);
            else if (op == "!=")
                predicate = new PropertyNotEquals(propertyType, value);
            else
                return new List<Error> { new Error(context.Start, "Unknown Expr op: " + op) };

            _predicates.Add(predicate);
            return null;
        }

        public override List<Error> VisitPath(storygenParser.PathContext context)
        {
            throw new System.NotImplementedException();
            var varId = context.VAR_ID();
            var ids = context.ID();
            if (ids.Length > 1)
                return new List<Error> { new(context.Start, "expected two parts, got " + context.GetText()) };

            Console.WriteLine("    VisitValue " + string.Join(", ", context.ID().Select(i => i.GetText())));
            _values.Add(new PropertyValue());
            return base.VisitPath(context);
        }
        
        private static PropertyType ParsePropertyType(ITerminalNode id)
        {

            return Enum.Parse<PropertyType>(id.GetText(), true);
        }

        (int varIndex, PropertyType prop) ParsePath(storygenParser.PathContext context)
        {
            int varIndex = 0;
            var varId = context.VAR_ID();
            if (varId != null)
            {
                varIndex = _variables.IndexOf(varId.GetText());
                if (varIndex == -1)
                    throw new System.NotImplementedException("Unknown var " + varId.GetText());

            }
            if (context.ID().Length != 1)
                throw new Exception("expected two parts, got ");

            PropertyType type = Enum.Parse<PropertyType>(context.ID(0).GetText(), true);
            return (varIndex, type);
        }
    }
}