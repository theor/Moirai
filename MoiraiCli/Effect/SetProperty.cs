using Pcg.Core;

public class SetProperty : IInstruction
{
    public readonly PropertyPath PropertySet;
    public readonly IValue Parameter;

    public SetProperty(PropertyPath property, IValue parameter)
    {
        PropertySet = property;
        Parameter = parameter;
    }
    public SetProperty(PropertyPath property, PropertyValue parameter)
    {
        PropertySet = property;
        Parameter = new Literal(parameter);
    }
   
    public bool Execute(PredicateContext ctx)
    {
        return ctx.Database.SetProperty(ctx.Argument(PropertySet.VariableIndex).IntValue, PropertySet.Property, Parameter.Compute(ctx));
    }
}