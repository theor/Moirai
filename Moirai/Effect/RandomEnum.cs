public struct RandomRange : IValueCall
{
    public readonly IValue Min, Max;

    public RandomRange(IValue min, IValue max)
    {
        Min = min;
        Max = max;
    }

    public PropertyValue Compute(PredicateContext ctx)
    {
        var max = Max.Compute(ctx).IntValue;
        var min = Min.Compute(ctx).IntValue;
        if (max - min <= 0)
            return min;
        return ctx.Rnd.GenerateNext((uint)(max - min)) + min;
    }

    public string ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return Min;
        yield return Max;
    }
}

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
