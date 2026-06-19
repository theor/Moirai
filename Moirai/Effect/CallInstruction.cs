using Moirai.Core;

public struct CallInstruction : IInstruction
{
    public readonly IValue Value;

    public SourceSpan Source { get; set; } = SourceSpan.None;

    public CallInstruction(IValue value)
    {
        Value = value;
    }

    public PropertyValue Execute(ExecuteContext ctx)
    {
        return Value.Compute(ctx);
    }
}
