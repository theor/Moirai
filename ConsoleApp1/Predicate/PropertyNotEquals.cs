public class PropertyNotEquals : IPredicate
{
    public readonly PropertyType Property;
    public readonly PredicateParameter Value;

    public PropertyNotEquals(PropertyType property, PredicateParameter value)
    {
        Property = property;
        Value = value;
    }
    public PropertyNotEquals(PropertyType property, PropertyValue value)
    {
        Property = property;
        Value = (PredicateParameter)value;
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity) && entity.GetProperty(Property) != Value.GetValue(ctx);
    }
}