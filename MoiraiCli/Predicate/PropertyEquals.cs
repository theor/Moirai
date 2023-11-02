public class BinaryOperator : IValue
{
    public enum Operator
    {
        Equals,
        NotEquals,
    }
    public readonly Operator Op;
    public readonly IValue Left;
    public readonly IValue Right;

  
    public BinaryOperator(Operator op, IValue left, PropertyValue value)
    {
        Op = op;
        Left = left;
        Right = new Literal(value);
    }
    public BinaryOperator(Operator op, IValue left, IValue right)
    {
        Op = op;
        Left = left;
        Right = right;
    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        if (!ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity))
            return false;

        var left = Left.Compute(ctx);
        var right = Right.Compute(ctx);
        switch (Op)
        {
            case Operator.Equals:
                return left == right;
            case Operator.NotEquals:
                return left != right;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}