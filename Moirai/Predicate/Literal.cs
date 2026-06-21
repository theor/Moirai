using Moirai.Core;

public struct Literal : IValueSql
{
    public readonly PropertyValue Value;
    public Literal(PropertyValue value)
    {
        Value = value;
    }
    public PropertyValue Compute(ExecuteContext ctx) => Value;
}
