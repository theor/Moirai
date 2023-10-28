class HasProperty : IPredicate
{
    public readonly PropertyType Property;

    public HasProperty(PropertyType property)
    {
        Property = property;
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity) && entity.TryGetProperty(Property, out _);
    }
}