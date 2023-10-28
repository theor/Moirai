class CreateEntity : IEffect
{
    public EntityType Type;
    public CreateEntity(EntityType type)
    {
        Type = type;
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        // if (!ctx.Database.EntityExists(ctx.EntityId))
        var entity = ctx.Database.AllocateEntity(Type);
        ctx.PushArgument(entity);
        return NameEntity.MakeTrue(ctx);
    }
}