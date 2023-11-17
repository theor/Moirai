public class BinaryOperator : IValue
{
    public enum Operator
    {
        Equals,
        NotEquals,
        Add,Sub,Div,Mul,
        Gt,Lt,
        Ge,Le,
    }
    public readonly Operator Op;
    public readonly IValue Left;
    public readonly IValue Right;

    public BinaryOperator(Operator op, IValue left, IValue right)
    {
        Op = op;
        Left = left;
        Right = right;
    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        var left = Left.Compute(ctx);
        Profiler.Value(left.Type.BaseType);
        var right = Right.Compute(ctx);
        Profiler.Value(right.Type.BaseType);
        switch (Op)
        {
            case Operator.Equals:
                return left == right;
            case Operator.NotEquals:
                return left != right;
            case Operator.Add:
                return left.FloatValue + right.FloatValue;
            case Operator.Sub:
                return left.FloatValue - right.FloatValue;
            case Operator.Div:
                return left.FloatValue / right.FloatValue;
            case Operator.Mul:
                return left.FloatValue * right.FloatValue;
            case Operator.Gt:
                return left.FloatValue > right.FloatValue;
            case Operator.Lt:
                return left.FloatValue < right.FloatValue;
            case Operator.Ge:
                return left.FloatValue >= right.FloatValue;
            case Operator.Le:
                return left.FloatValue <= right.FloatValue;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = default;
        return false;
    }
    public string ToSql(PredicateContext ctx)
    {
        var l = Left.ToSql(ctx);
        var r = Right.ToSql(ctx);
        string op = Op switch
        {

            Operator.Equals => "=",
            Operator.NotEquals => "!=",
            Operator.Add => "+",
            Operator.Sub => "-",
            Operator.Div => "/",
            Operator.Mul => "*",
            Operator.Gt => ">",
            Operator.Lt => "<",
            Operator.Ge => ">=",
            Operator.Le => "<=",
            _ => throw new ArgumentOutOfRangeException()
        };
        return $"({l} {op} {r})";
    }
}
