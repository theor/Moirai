using Moirai.Core;

/// `repeat(n) { ... }`: runs the block n times, n read once before the first turn. Each turn is a scope of
/// its own, like an `each` iteration, and a statement that stops (a failed pick) ends that turn only -- so
/// `repeat(10) { found_city() }` behaves like ten separate calls.
/// It draws no random numbers of its own.
public struct Repeat : IValueCall
{
    public readonly IValue Times;
    public readonly IInstruction[] Body;

    public Repeat(IValue times, IInstruction[] body)
    {
        Times = times;
        Body = body;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var n = Times.Compute(ctx).IntValue;
        for (int i = 0; i < n; i++)
        {
            using var turn = ctx.RunScope(false);
            foreach (var instr in Body)
            {
                ctx.Database.DebugHook?.OnStatement(instr, ctx);
                if (!instr.Execute(ctx).BoolValue)
                {
                    // Stopping ends this turn only, whether or not it was handled.
                    ctx.HandledStop = false;
                    break;
                }
            }
        }

        return true;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    public string Print(StoryPrinter printer, int indent)
    {
        var b = new System.Text.StringBuilder($"repeat({printer.Print(Times)})");
        b.AppendLine(" {");
        foreach (var effect in Body)
            printer.PrintEffect(effect, b, indent + 1);
        b.Append(StoryPrinter.IndentStr(indent) + "}");
        return b.ToString();
    }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return Times;
    }
}
