public struct CallInstruction : IInstruction
{
    public readonly IValue Value;

    public CallInstruction(IValue value)
    {
        Value = value;
    }

    public PropertyValue Execute(PredicateContext ctx)
    {
        return Value.Compute(ctx);
    }
}
