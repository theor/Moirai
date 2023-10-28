public class SetProperty : IEffect
{
    public readonly PropertyType Property;
    public readonly int Target;
    public readonly PredicateParameter Parameter;

    public SetProperty(PropertyType property, PredicateParameter parameter)
    {
        Property = property;
        Parameter = parameter;
        Target = 0;
    }
    public SetProperty(PropertyType property, PropertyValue parameter)
    {
        Property = property;
        Parameter = parameter;
        Target = 0;
    }
    public SetProperty(int target, PropertyType property, PredicateParameter parameter)
    {
        Property = property;
        Parameter = parameter;
        Target = target;
    }
    public SetProperty(int target, PropertyType property, PropertyValue parameter)
    {
        Property = property;
        Parameter = parameter;
        Target = target;
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        return ctx.Database.SetProperty(ctx.Argument(Target), Property, Parameter.GetValue(ctx));
    }
}