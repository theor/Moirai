using Pcg.Core;

public class FormatAction : IInstruction
{
    public readonly string FormatString;
    public PropertyPath[] Arguments;
    public FormatAction(string formatString, PropertyPath[] arguments)
    {
        FormatString = formatString;
        Arguments = arguments;
    }
    public bool Execute(PredicateContext ctx)
    {
        ctx.Format(this);
        return true;
    }
}

class CreateEntity : IInstruction
{
    public readonly int VariableIndex;
    public EntityTypeId Type;
    public CreateEntity(int variableIndex, EntityTypeId type)
    {
        VariableIndex = variableIndex;
        Type = type;
    }
    public bool Execute(PredicateContext ctx)
    {
        // if (!ctx.Database.EntityExists(ctx.EntityId))
        string name = NameEntity.MakeName(ctx, Type);
        var entity = ctx.Database.AllocateEntity(Type, name);
        ctx.SetArgument(VariableIndex, entity);
        return true;
    }
}