using System.Reflection;

public class BinaryOperator : IValue
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
    public PropertyValue Compute(PredicateContext ctx)
    {
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
                case Operator.And:
                    return left.BoolValue && right.BoolValue;
                case Operator.Or:
                    return left.BoolValue || right.BoolValue;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        var (l,lj) = Left.ToSql(ctx);
        var (r,rj) = Right.ToSql(ctx);
        var joins = string.Join("", new[]{lj, rj}.Where(s => s!=null));
        if (Op == Operator.Coalesce)
            return ($"COALESCE({l}, {r})", joins);
        if (Op == Operator.Equals || Op == Operator.NotEquals)
        {
            if(l == "null")
                return ($"({r} IS{(Op == Operator.NotEquals ? " NOT" : "")} NULL)", joins);
            if(r == "null")
                return ($"({l} IS{(Op == Operator.NotEquals ? " NOT" : "")} NULL)", joins);
        }
        string op = Op switch
        {

            Operator.And => "and",
            Operator.Or => "or",
            Operator.Equals => "=",
            Operator.NotEquals => "!=",
            Operator.Add => "+",
            Operator.Sub => "-",
            Operator.Div => "/",
            Operator.Mul => "*",
            Operator.Mod => "%",
            Operator.Gt => ">",
            Operator.Lt => "<",
            Operator.Ge => ">=",
            Operator.Le => "<=",
            _ => throw new ArgumentOutOfRangeException()
        };
        return ($"({l} {op} {r})", joins);
    }
}
