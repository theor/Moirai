using Moirai.Core;

public class BinaryOperator : IValueSql
{
    public enum Operator
    {
        And, Or,
        Equals,
        NotEquals,
        Add,Sub,Div,Mul,
        Mod,
        Gt,Lt,
        Ge,Le,
        Coalesce,
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
    public PropertyValue Compute(ExecuteContext ctx)
    {
        // `and`/`or` stop at the first side that settles the answer. Evaluating both always made every
        // pick pay for its most expensive clause on every candidate — the dead, the wrong place, the
        // wrong age included — and a story orders its clauses expecting the cheap ones to filter first.
        // (The dedicated And class already did this.)
        if (Op == Operator.And)
            return Left.Compute(ctx).BoolValue && Right.Compute(ctx).BoolValue;
        if (Op == Operator.Or)
            return Left.Compute(ctx).BoolValue || Right.Compute(ctx).BoolValue;

        var left = Left.Compute(ctx);
        Profiler.Value(left.Type.BaseType);
        var right = Right.Compute(ctx);
        Profiler.Value(right.Type.BaseType);
        var propertyValue = DoCompute();
        if (left.Type.BaseType == PropertyValue.ValueBaseType.Percentage &&
            right.Type.BaseType == PropertyValue.ValueBaseType.Percentage)
            return new PropertyValue(PropertyValue.TypePercent, propertyValue.FloatValue);
        return propertyValue;

        PropertyValue DoCompute()
        {
            switch (Op)
            {
                case Operator.Coalesce:
                    return left.IntValue == 0 ? right : left;
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
                case Operator.Mod:
                    return left.IntValue % right.IntValue;
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
    }

}
