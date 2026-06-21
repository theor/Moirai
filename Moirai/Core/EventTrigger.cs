using Moirai.Core;

public class EventTrigger(int id, string name, bool isEvent,IFilter? filter, bool skip = false, List<string>? tags = null)
{
    public List<string>? Tags { get; } = tags;

    public enum WhenType { Created, Changed, }
    public readonly int Id = id;
    public readonly string Name = name;
    public readonly bool IsTrigger = isEvent;
    public bool Skip = skip;

    public readonly List<IInstruction> Effects = new();
    // Parameters for an event invoked as call(name, args...). Declared as the event scope's first
    // value-stack slots (0..n-1), which call() binds before the body runs. Null = no parameters.
    public List<FunctionDefinition.Parameter>? Parameters;
    public (WhenType, EntityTypeId, IValue?) When = default;

    public readonly IFilter? Filter = filter;
    // public List<TagId> WhenTags = new();

    /// Lexical variable-scope tree for the debugger (null when not parsed for debugging info).
    public DebugScope? DebugScopeRoot;
}

/// <summary>
/// A registered <c>schedule(...) { body }</c> site: the deferred body compiled as an <see cref="EventTrigger"/>
/// (so it fires through <see cref="Moirai.Core.Database.RunAction"/>, reusing changeset/history/trigger replay),
/// plus the value-stack slot the bound entity (<c>$self</c>) must be written to before firing.
/// </summary>
public class ScheduleSite(EventTrigger trigger, int selfVarIndex)
{
    public readonly EventTrigger Trigger = trigger;
    public readonly int SelfVarIndex = selfVarIndex;
}
