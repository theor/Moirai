public class PropertyNotEquals : IPredicate
{
    public readonly PropertyType Property;
    public readonly ComputedValue Value;

    public PropertyNotEquals(PropertyType property, ComputedValue value)
    {
        Property = property;
        Value = value;
    }
    public PropertyNotEquals(PropertyType property, PropertyValue value)
    {
        Property = property;
        Value = (ComputedValue)value;
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity) && entity.GetProperty(Property) != ctx.GetValue(Value);
    }
}