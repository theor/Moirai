public struct PredicateParameter : IEffect
{
    public enum PredicateParameterType
    {
        Value,
        Predicate,
        Argument,
    }

    public readonly PredicateParameterType Type;
    public readonly IPredicate? Predicate;
    public readonly PropertyValue Value;
    public int ArgumentIndex = 0;
    public PredicateParameter(IPredicate predicate) : this()
    {
        Predicate = predicate;
        Type = PredicateParameterType.Predicate;
    }
    public PredicateParameter(PropertyValue value) : this()
    {
        Value = value;
        Type = PredicateParameterType.Value;
    }

    private PredicateParameter(int argumentIdx) : this()
    {
        ArgumentIndex = argumentIdx;
        Type = PredicateParameterType.Argument;
    }

    public static PredicateParameter Argument(int idx) => new PredicateParameter(idx);
    public static explicit operator PredicateParameter(PropertyValue v) => new PredicateParameter(v);
    public readonly PropertyValue GetValue(PredicateContext ctx)
    {
        switch (Type)
        {

            case PredicateParameterType.Value:
                return Value;
            case PredicateParameterType.Predicate:
                return ctx.Query(Predicate, out var val) ? val : default;
            case PredicateParameterType.Argument:
                return ctx.Argument(ArgumentIndex);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        ctx.SetArgument(ArgumentIndex, ctx.Query(Predicate, out var val) ? val : default);
        return true;
    }
}