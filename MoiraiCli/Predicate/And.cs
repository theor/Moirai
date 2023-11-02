public class And : IValue
{
    public List<IValue> Predicates = new();
    public And(params IValue[] predicates)
    {
        Predicates.AddRange(predicates);
    }
    public And(List<IValue> predicates)
    {
        Predicates.AddRange(predicates);
    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return Predicates.All(p => p.IsTrue(ctx));
    }
}