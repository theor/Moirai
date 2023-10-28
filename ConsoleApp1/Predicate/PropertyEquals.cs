public class PropertyEquals : IPredicate
{
    public readonly PropertyType Property;
    public readonly PredicateParameter Value;

    public PropertyEquals(EntityType type)
    {
        Property = PropertyType.Type;
        Value = (PropertyValue)(int)type;
    }
    public PropertyEquals(PropertyType property, PropertyValue value)
    {
        Property = property;
        Value = value;
    }
    public PropertyEquals(PropertyType property, PredicateParameter value)
    {
        Property = property;
        Value = value;
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity) && entity.GetProperty(Property) == Value.GetValue(ctx);
    }
}