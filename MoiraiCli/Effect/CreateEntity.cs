using Pcg.Core;

public class FormatAction : IInstruction
{
    public readonly string FormatString;
    public IValue[] Arguments;
    public FormatAction(string formatString, IValue[] arguments)
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
    public readonly EntityTypeId Type;
    public readonly string? Name;
    public CreateEntity(int variableIndex, EntityTypeId type, string? name)
    {
        VariableIndex = variableIndex;
        Type = type;
        Name = name;
    }
    public bool Execute(PredicateContext ctx)
    {
        // if (!ctx.Database.EntityExists(ctx.EntityId))
        string name = Name ?? NameEntity.MakeName(ctx, Type);
        var entity = ctx.Database.AllocateEntity(Type, name);
        ctx.SetArgument(VariableIndex, entity);
        return true;
    }
}