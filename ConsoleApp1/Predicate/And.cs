public class And : IPredicate
{
    public List<IPredicate> Predicates = new();
    public And(params IPredicate[] predicates)
    {
        Predicates.AddRange(predicates);
    }
    public And(List<IPredicate> predicates)
    {
        Predicates.AddRange(predicates);
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return Predicates.All(p => p.IsTrue(ctx));
    }
}