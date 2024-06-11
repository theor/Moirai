using Moirai.Core;
using ExecutionContext = Moirai.Core.ExecutionContext;

public struct CallInstruction : IInstruction
{
    public readonly IValue Value;

    public CallInstruction(IValue value)
    {
        Value = value;
    }

    public PropertyValue Execute(ExecutionContext ctx)
    {
        return Value.Compute(ctx);
    }
}
