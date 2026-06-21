using Moirai.Core;

public struct CallRule : IValueCall
{
    public readonly int RuleIndex;
    public readonly int Count;
    // Arguments for a parameterized event; null for the plain count-repeat form.
    public readonly IValue[]? Args;

    public CallRule(int eventIndex, int count)
    {
        RuleIndex = eventIndex;
        Count = count;
        Args = null;
    }

    public CallRule(int eventIndex, IValue[] args)
    {
        RuleIndex = eventIndex;
        Count = 1;
        Args = args;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        // DONE offset value stack
        // eg. if $0 $1 are used now, have called.$0 become $2
        // copy result in VariableIndex then pop extra values
        bool res = false;
        PropertyValue ctxLastValue = default;

        if (Args != null)
        {
            // Evaluate arguments in the CALLER's frame first (they reference the caller's locals),
            // then open the callee frame and write them into its parameter slots (0..n-1).
            var argv = new PropertyValue[Args.Length];
            for (int a = 0; a < Args.Length; a++)
                argv[a] = Args[a].Compute(ctx);

            using (ctx.RunScope(true))
            {
                for (int a = 0; a < argv.Length; a++)
                    ctx.SetArgument(a, argv[a]);
                res = ctx.Database.RunAction(ctx.Database.Actions[RuleIndex]);
                ctxLastValue = ctx.LastValue;
            }

            return ctxLastValue;
        }

        for (int i = 0; i < Count; i++)
            using (ctx.RunScope(true))
            {
                res = ctx.Database.RunAction(ctx.Database.Actions[RuleIndex]);
                ctxLastValue = ctx.LastValue;
            }
        return ctxLastValue;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return new Literal(printer.GetRuleName(RuleIndex));
        if (Args != null)
        {
            foreach (var a in Args)
                yield return a;
        }
        else
            yield return new Literal(Count);
    }
}
