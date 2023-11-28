public struct CallRule : IValueCall
{
    public readonly int RuleIndex;
    public readonly int Count;
    public CallRule(int eventIndex, int count)
    {
        RuleIndex = eventIndex;
        Count = count;

    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        // DONE offset value stack
        // eg. if $0 $1 are used now, have called.$0 become $2
        // copy result in VariableIndex then pop extra values
        bool res = false;
        PropertyValue ctxLastValue = default;
        for (int i = 0; i < Count; i++)
            using (ctx.RunScope())
            {
                res = ctx.Database.RunAction(ctx.Database.Actions[RuleIndex]);
                ctxLastValue = ctx.LastValue;
            }
        return ctxLastValue;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return new Literal(printer.GetRuleName(RuleIndex));
        yield return new Literal(Count);
    }
}
