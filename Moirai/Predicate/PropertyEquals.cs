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
                return left.IntValue + right.IntValue;
            case Operator.Sub:
                return left.IntValue - right.IntValue;
            case Operator.Div:
                return left.IntValue / right.IntValue;
            case Operator.Mul:
                return left.IntValue * right.IntValue;
            case Operator.Gt:
                return left.IntValue > right.IntValue;
            case Operator.Lt:
                return left.IntValue < right.IntValue;
            case Operator.Ge:
                return left.IntValue >= right.IntValue;
            case Operator.Le:
                return left.IntValue <= right.IntValue;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = default;
        return false;
    }
}