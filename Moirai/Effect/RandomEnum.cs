using Moirai.Core;

public struct RandomRange : IValueCall
{
    public readonly IValue Min, Max;

    public RandomRange(IValue min, IValue max)
    {
        Min = min;
        Max = max;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var max = Max.Compute(ctx).IntValue;
        var min = Min.Compute(ctx).IntValue;
        if (max - min <= 0)
            return min;
        return ctx.Rnd.GenerateNext((uint)(max - min)) + min;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return Min;
        yield return Max;
    }
}

public struct RollTable : IValueCall
{
    public readonly int TableId;
    public readonly string TableName;
    public RollTable(int tableId, string tableName)
    {
        TableId = tableId;
        TableName = tableName;
    }

    public PropertyValue Compute(ExecuteContext ctx) => ctx.Database.Tables[TableId].Roll(ctx);

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    // Override the descriptor-based default (which can't render a no-arg call).
    public string Print(StoryPrinter printer, int indent) => $"roll({TableName})";
    public IEnumerable<IValue> GetArgs(StoryPrinter printer) { yield break; }
}

public struct RandomEnum : IValueCall
{
    public readonly EnumDefinitionId EnumID;
    public RandomEnum(EnumDefinitionId enumId)
    {
        EnumID = enumId;
    }
    public PropertyValue Compute(ExecuteContext ctx)
    {
        var def = ctx.Database.Enums[EnumID.Id];
        return def.GetRandomValue(ctx.Rnd);
    }

    //public (string where, string? joins) ToSql(ExecuteContext ctx)
    //{
    //    return (Compute(ctx).ToSql(), null);
    //}

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return new Literal(EnumID);
    }
}
