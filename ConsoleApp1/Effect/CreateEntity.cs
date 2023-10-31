using Pcg.Core;

public class FormatAction : IEffect
{
    public readonly string FormatString;
    public PropertyPath[] Arguments;
    public FormatAction(string formatString, PropertyPath[] arguments)
    {
        FormatString = formatString;
        Arguments = arguments;
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }
}

class CreateEntity : IEffect
{
    public readonly int VariableIndex;
    public EntityType Type;
    public CreateEntity(int variableIndex, EntityType type)
    {
        VariableIndex = variableIndex;
        Type = type;
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        // if (!ctx.Database.EntityExists(ctx.EntityId))
        string name = NameEntity.MakeName(ctx, Type);
        var entity = ctx.Database.AllocateEntity(Type, name);
        ctx.SetArgument(VariableIndex, entity);
        return true;
    }
}