public struct CallInstruction : IInstruction
{
    public readonly IValue Value;

    public CallInstruction(IValue value)
    {
        Value = value;
    }

    public bool Execute(PredicateContext ctx)
    {
        return Value.Compute(ctx).BoolValue;
    }
}