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
    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = default;
        return false;
    }
    public string ToSql(PredicateContext ctx)
    {
        return Value.ToSql();
    }
}
