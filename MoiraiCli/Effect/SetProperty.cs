using Pcg.Core;

public class SetProperty : IEffect
{
    public readonly PropertyPath PropertySet;
    public readonly ComputedValue Parameter;

    public SetProperty(PropertyPath property, ComputedValue parameter)
    {
        PropertySet = property;
        Parameter = parameter;
    }
    public SetProperty(PropertyPath property, PropertyValue parameter)
    {
        PropertySet = property;
        Parameter = (ComputedValue)parameter;
    }
   
    public bool MakeTrue(PredicateContext ctx)
    {
        return ctx.Database.SetProperty(ctx.Argument(PropertySet.VariableIndex).IntValue, PropertySet.Property, ctx.GetValue(Parameter));
    }
}

public struct PropertyPath
{
    public PropertyPath(int variableIndex, PropertyId? property = null)
    {
        VariableIndex = variableIndex;
        Property = property ?? PropertyId.Null;
    }
    // public enum PathSegmentType
    // {
    //     Variable, Property,
    // }

    public int VariableIndex;
    public PropertyId Property;
}