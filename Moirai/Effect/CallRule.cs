using Moirai.Core;

public struct CallRule : IValueCall
{
    public readonly int RuleIndex;
    public readonly int Count;
    // Arguments for a parameterized event; null for the plain count-repeat form.
    public readonly IValue[]? Args;
    // Reused across calls, and taken while in use, so a callee that reaches this same site again (recursion)
    // evaluates into an array of its own.
    private PropertyValue[]? _argv;

    public CallRule(int eventIndex, int count)
    {
        RuleIndex = eventIndex;
        Count = count;
        Args = null;
        _argv = null;
    }

    public CallRule(int eventIndex, IValue[] args)
    {
        RuleIndex = eventIndex;
        Count = 1;
        Args = args;
        _argv = null;
    }

    /// Runs the event as a rule of its own. Its success is its own too: a callee that fails (a pick that
    /// finds nobody) discards its own changes, and the caller carries on. This used to return whatever value
    /// was last on the stack -- often the callee's last local, sometimes nothing -- so whether the caller went
    /// on after a call was an accident of what the callee declared.
    public PropertyValue Compute(ExecuteContext ctx)
    {
        if (Args != null)
        {
            // Evaluate arguments in the CALLER's frame first (they reference the caller's locals),
            // then open the callee frame and write them into its parameter slots (0..n-1).
            var argv = _argv ?? new PropertyValue[Args.Length];
            _argv = null;
            for (int a = 0; a < Args.Length; a++)
                argv[a] = Args[a].Compute(ctx);

            using (ctx.RunScope(true))
            {
                for (int a = 0; a < argv.Length; a++)
                    ctx.SetArgument(a, argv[a]);
                Array.Clear(argv);
                _argv = argv;
                ctx.Database.RunAction(ctx.Database.Actions[RuleIndex]);
            }

            return true;
        }

        for (int i = 0; i < Count; i++)
            using (ctx.RunScope(true))
                ctx.Database.RunAction(ctx.Database.Actions[RuleIndex]);
        return true;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    /// An event is called by name: `name(args)`.
    public string Print(StoryPrinter printer, int indent) =>
        $"{printer.GetRuleName(RuleIndex)}({string.Join(", ", (Args ?? []).Select(a => printer.Print(a)))})";

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
