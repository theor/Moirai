using Moirai.Core;

public class EventTrigger(int id, string name, bool isEvent,IFilter? filter, bool skip = false, List<string>? tags = null)
{
    public List<string>? Tags { get; } = tags;

    public enum WhenType { Created, Changed, }
    public readonly int Id = id;
    public readonly string Name = name;

    // Stable id of this rule's RNG stream (FNV-1a of the name), so every event/trigger draws from its
    // own independent PCG stream — adding or reordering rules doesn't perturb others' randomness.
    private ulong _rngStreamId;
    public ulong RngStreamId
    {
        get
        {
            if (_rngStreamId == 0)
            {
                ulong h = 14695981039346656037UL;
                foreach (char c in Name)
                    h = (h ^ c) * 1099511628211UL;
                _rngStreamId = h == 0 ? 1UL : h;
            }

            return _rngStreamId;
        }
    }
    public readonly bool IsTrigger = isEvent;
    public bool Skip = skip;

    // Cumulative firing counters for the whole life of this world (events: invocations and completions;
    // triggers: evaluations and predicate matches). Unlike ExecutionProfiler these are not reset per
    // PassYears run and cost two increments with no timestamp, so they are always on: a rule that never
    // fires is a bug in the story, and finding that out should not require running with --profile.
    public long Attempts;
    public long Successes;

    public readonly List<IInstruction> Effects = new();

    // Property gating for `when Changed` triggers: the set of THIS entity-type's properties the
    // predicate reads. A changed entity only needs this trigger re-evaluated when one of these actually
    // changed (see Database.RunTriggers). null = "always evaluate" — either no predicate, or a predicate
    // using constructs we don't statically analyse (function calls etc.), so we stay conservative.
    // Computed lazily and cached.
    public PropertyId[]? GatingProps;
    public bool GatingComputed;
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
