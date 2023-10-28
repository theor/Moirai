public class PropertyEquals : IPredicate
{
    public readonly PropertyType Property;
    public readonly ComputedValue Value;

    public PropertyEquals(EntityType type)
    {
        Property = PropertyType.Type;
        Value = (ComputedValue)(PropertyValue)(int)type;
    }
    public PropertyEquals(PropertyType property, PropertyValue value)
    {
        Property = property;
        Value = (ComputedValue)value;
    }
    public PropertyEquals(PropertyType property, ComputedValue value)
    {
        Property = property;
        Value = value;
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity) && entity.GetProperty(Property) == ctx.GetValue(Value);
    }
}