using Moirai.Core;

/// <summary>
/// Backs <c>call(name, count)</c> when <c>name</c> is a (procedural) function rather than an event:
/// runs the no-argument function <paramref name="Def"/> <see cref="Count"/> times in the caller's
/// context. Unlike <see cref="CallRule"/> (which runs an event via RunAction, opening its own
/// changeset and firing triggers), a function's effects run within the current changeset.
/// </summary>
public struct CallFunction : IValueCall
{
    public readonly FunctionDefinition Def;
    public readonly int Count;

    public CallFunction(FunctionDefinition def, int count)
    {
        Def = def;
        Count = count;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var call = new UserFunctionCall(Def, System.Array.Empty<IValue>());
        PropertyValue last = default;
        for (int i = 0; i < Count; i++)
            last = call.Compute(ctx);
        return last;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return new Literal(Def.Name);
        yield return new Literal(Count);
    }
}
