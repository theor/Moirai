using Moirai.Core;

public class InterpolatedString : IValue
{
    public readonly string FormatString;
    public IValue[] Arguments;
    public InterpolatedString(string formatString, IValue[] arguments)
    {
        FormatString = formatString;
        Arguments = arguments;
    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        return ctx.Database.Printer.Format(this, ctx.Database) ?? "";
    }
    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = default;
        return false;
    }
    public string ToSql(PredicateContext ctx) => $"'{Compute(ctx)}'";
}
public class FormatAction : IInstruction
{
    public InterpolatedString String;
    public FormatAction(InterpolatedString str)
    {
        String = str;
    }
    public bool Execute(PredicateContext ctx)
    {
        ctx.Database.CurrentChangeset.AppendDescription(ctx.Database.Printer.Format(String, ctx.Database, true));
        return true;
    }
}

public class CreateEntity : IInstruction
{
    public readonly int VariableIndex;
    public readonly EntityTypeId Type;
    public readonly InterpolatedString? Name;
    public CreateEntity(int variableIndex, EntityTypeId type, InterpolatedString? name)
    {
        VariableIndex = variableIndex;
        Type = type;
        Name = name;
    }
    public bool Execute(PredicateContext ctx)
    {
        // if (!ctx.Database.EntityExists(ctx.EntityId))
        string? name = null;
        if (Name != null)
        {
            name = ctx.Database.Printer.Format(Name, ctx.Database);
        }
      
        var entity = ctx.Database.AllocateEntity(Type, name);
        ctx.SetArgument(VariableIndex, entity);
        return true;
    }
}