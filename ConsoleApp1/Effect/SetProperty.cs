public class SetProperty : IEffect
{
    public readonly PropertyPath PropertySet;
    public readonly PredicateParameter Parameter;

    public SetProperty(PropertyPath property, PredicateParameter parameter)
    {
        PropertySet = property;
        Parameter = parameter;
    }
    public SetProperty(PropertyPath property, PropertyValue parameter)
    {
        PropertySet = property;
        Parameter = (PredicateParameter)parameter;
    }
   
    public bool MakeTrue(PredicateContext ctx)
    {
        return ctx.Database.SetProperty(ctx.Argument(PropertySet.VariableIndex).IntValue, PropertySet.Property, Parameter.GetValue(ctx));
    }
}

public struct PropertyPath
{
    public PropertyPath(int variableIndex, PropertyType property)
    {
        VariableIndex = variableIndex;
        Property = property;
    }
    // public enum PathSegmentType
    // {
    //     Variable, Property,
    // }

    public int VariableIndex;
    public PropertyType Property;
}