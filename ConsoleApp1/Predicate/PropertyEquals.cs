public class PropertyOperator : IPredicate
{
    public enum Operator
    {
        Equals,
        NotEquals,
    }
    public readonly Operator Op;
    public readonly PropertyType Property;
    public readonly ComputedValue Value;

    public PropertyOperator(Operator op, EntityType type)
    {
        Op = op;
        Property = PropertyType.Type;
        Value = (ComputedValue)(PropertyValue)(int)type;
    }
    public PropertyOperator(Operator op, PropertyType property, PropertyValue value)
    {
        Op = op;
        Property = property;
        Value = (ComputedValue)value;
    }
    public PropertyOperator(Operator op, PropertyType property, ComputedValue value)
    {
        Op = op;
        Property = property;
        Value = value;
    }
    public bool IsTrue(PredicateContext ctx)
    {
        if (!ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity))
            return false;

        var left = entity.GetProperty(Property);
        var right = ctx.GetValue(Value);
        switch (Op)
        {
            case Operator.Equals:
                return left == right;
            case Operator.NotEquals:
                return left != right;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}