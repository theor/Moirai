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

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return new Literal(EnumID);
    }
}

/// `chance(p)`: true with probability p, a percentage (`chance(3%)`). Always draws exactly once, so a
/// world moves the same way whatever p is. With a body (`chance(3%) { ... }`) it is a statement: the
/// body runs when the draw hits, and a miss is not a failure -- the rule carries on, which a bare
/// `false` in statement position would not do.
public struct Chance : IValueCall
{
    /// Draws are taken out of this many, so a probability keeps two decimals of a percent.
    const uint Resolution = 10_000;

    public readonly IValue Probability;
    public readonly IInstruction[]? Body;

    public Chance(IValue probability, IInstruction[]? body)
    {
        Probability = probability;
        Body = body;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var threshold = Probability.Compute(ctx).FloatValue * (Resolution / 100);
        bool hit = ctx.Rnd.GenerateNext(Resolution) < threshold;
        if (Body == null)
            return hit;
        if (!hit)
            return true;

        PropertyValue res = true;
        foreach (var instr in Body)
        {
            ctx.Database.DebugHook?.OnStatement(instr, ctx);
            res = instr.Execute(ctx);
            if (!res.BoolValue)
                break;
        }

        return res;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    public string Print(StoryPrinter printer, int indent)
    {
        var s = $"chance({printer.Print(Probability)})";
        if (Body == null)
            return s;
        var b = new System.Text.StringBuilder(s);
        b.AppendLine(" {");
        foreach (var effect in Body)
            printer.PrintEffect(effect, b, indent + 1);
        b.Append(StoryPrinter.IndentStr(indent) + "}");
        return b.ToString();
    }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return Probability;
    }
}
