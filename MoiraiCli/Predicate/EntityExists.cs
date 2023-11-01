class EntityExists : IPredicate
{
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.EntityExists(ctx.EntityId);
    }
}