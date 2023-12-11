using Microsoft.VisualBasic;
using Moirai.Core;

public struct Literal : IValue
{
    public readonly PropertyValue Value;
    public Literal(PropertyValue value)
    {
        Value = value;
    }
    public PropertyValue Compute(PredicateContext ctx) => Value;

    public (string where, string joins) ToSql(PredicateContext ctx)
    {
        return (Value.ToSql(), null);
    }
}
