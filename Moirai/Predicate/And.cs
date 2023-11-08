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
    public bool HasTypeFilter(out EntityTypeId type)
    {
        return Predicates[0].HasTypeFilter(out type);
    }
    public bool IsTrue(PredicateContext ctx)
    {
        foreach (var p in Predicates)
        {
            if (!p.IsTrue(ctx))
                return false;
        }
        return true;
    }
}