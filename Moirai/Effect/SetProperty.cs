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
   
    public PropertyValue Execute(ExecuteContext ctx)
    {
        if (IsLocalVar)
        {
            ctx.SetArgument(PropertySet.VariableIndex, Parameter.Compute(ctx));
            return true;
        }

        EntityId eid;
        if (PropertySet.Mode == PropertyPath.PropertyPathMode.Singleton)
        {
            eid = ctx.GetSingletonId(PropertySet.SingletonTypeId);
        }
        else
        {
            eid = ctx.Argument(PropertySet.VariableIndex).Id;
        }

        for (int i = 0; i < PropertySet.Segments.Count - 1; i++)
        {
            if (!ctx.Database.GetProperty(eid, PropertySet.Segments[i].Property, out var v) || !v.Type.IsRefType)
                throw new InvalidOperationException("path link is not a ref");
            eid = v.Id;
        }
        return ctx.Database.SetProperty(eid, PropertySet.Segments[^1].Property, Parameter.Compute(ctx));
    }
}
