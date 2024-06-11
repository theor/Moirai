using Microsoft.VisualBasic;
using Moirai.Core;
using ExecutionContext = Moirai.Core.ExecutionContext;

public struct Literal : IValue
{
    public readonly PropertyValue Value;
    public Literal(PropertyValue value)
    {
        Value = value;
    }
    public PropertyValue Compute(ExecutionContext ctx) => Value;

    public (string where, string joins) ToSql(ExecutionContext ctx)
    {
        return (Value.ToSql(), null);
    }
}
