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
        var entity = ctx.Database.AllocateEntity(Type);
        ctx.SetArgument(VariableIndex, entity);
        return NameEntity.MakeTrue(ctx);
    }
}