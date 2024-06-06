using Moirai.Core;

public class SetProperty : IInstruction
{
    public readonly bool IsLocalVar;
    public readonly PropertyPath PropertySet;
    public readonly IValue Parameter;

    public SetProperty(PropertyPath property, IValue parameter, bool isLocalVar)
    {
        IsLocalVar = isLocalVar;
        PropertySet = property;
        Parameter = parameter;
    }
   
    public PropertyValue Execute(PredicateContext ctx)
    {
        if (IsLocalVar)
        {
            ctx.SetArgument(PropertySet.VariableIndex, Parameter.Compute(ctx));
            return true;
        }

        EntityId eid;
        if (PropertySet.Mode == PropertyPath.PropertyPathMode.Singleton)
        {
            eid = ctx.GetSingletonId(PropertySet.Property[0].TypeId);
        }
        else
        {
            eid = ctx.Argument(PropertySet.VariableIndex).Id;
        }

        for (int i = 0; i < PropertySet.Property.Count - 1; i++)
        {
            if (!ctx.Database.GetProperty(eid, PropertySet.Property[i], out var v) || !v.Type.IsRefType)
                throw new InvalidOperationException("path link is not a ref");
            eid = v.Id;
        }
        return ctx.Database.SetProperty(eid, PropertySet.Property[^1], Parameter.Compute(ctx));
    }
}
