public struct RandomEnum : IValueCall
{
    public readonly EnumDefinitionId EnumID;
    public RandomEnum(EnumDefinitionId enumId)
    {
        EnumID = enumId;
    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        var def = ctx.Database.Enums[EnumID.Id];
        return def.GetRandomValue(ctx.Rnd);
    }
    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = default;
        return false;
    }
    public string ToSql(PredicateContext ctx)
    {
        return Compute(ctx).ToSql();
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return new Literal(EnumID);
    }
}