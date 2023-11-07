using Moirai.Core;

public class SetProperty : IInstruction
{
    // TODO use to cast
    public PropertyValue.ValueType ValueType;
    public readonly bool IsLocalVar;
    public readonly PropertyPath PropertySet;
    public readonly IValue Parameter;

    public SetProperty(PropertyPath property, IValue parameter, bool isLocalVar, PropertyValue.ValueType valueType)
    {
        ValueType = valueType;
        IsLocalVar = isLocalVar;
        PropertySet = property;
        Parameter = parameter;
    }
   
    public bool Execute(PredicateContext ctx)
    {
        if (IsLocalVar)
        {
            ctx.SetArgument(PropertySet.VariableIndex, Parameter.Compute(ctx));
            return true;
        }
        if (PropertySet.Mode == PropertyPath.PropertyPathMode.Singleton)
        {
            return ctx.Database.SetProperty(ctx.GetSingletonId(PropertySet.SingletonType), PropertySet.Property, Parameter.Compute(ctx));
        }
        return ctx.Database.SetProperty(ctx.Argument(PropertySet.VariableIndex).Id, PropertySet.Property, Parameter.Compute(ctx));
    }
}