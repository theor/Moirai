namespace Moirai.Core;

public enum DebugFrameKind
{
    Event,
    Trigger,
    Function,
}

/// <summary>
/// Optional per-statement execution hook for a step-through debugger. Mirrors the
/// null-checked, opt-in pattern of <see cref="ExecutionProfiler"/>: when
/// <see cref="Database.DebugHook"/> is null there is zero overhead.
///
/// The engine only *reports* execution through this interface — the actual
/// suspend/resume and stepping logic lives in the implementation (the DAP server),
/// which typically blocks the simulation thread inside <see cref="OnStatement"/> until
/// the user resumes.
/// </summary>
public interface IDebugHook
{
    /// <summary>
    /// A new event/trigger/function body is starting. <paramref name="valueOffset"/> is the
    /// value-stack base (<see cref="ExecuteContext.ValueOffset"/>) used to read this frame's
    /// locals; <paramref name="scope"/> is its lexical variable tree (may be null).
    /// </summary>
    void OnEnterFrame(DebugFrameKind kind, string name, DebugScope? scope, int valueOffset);

    /// <summary>The most recently entered frame is finishing.</summary>
    void OnExitFrame();

    /// <summary>
    /// About to execute one statement. Implementations may block here to honor a
    /// breakpoint or an in-progress step.
    /// </summary>
    void OnStatement(IInstruction instruction, ExecuteContext ctx);

    /// <summary>
    /// A <c>record(...)</c> statement just produced its interpolated <paramref name="text"/> (the same
    /// string appended to world history) at simulation <paramref name="year"/>. Fired on the simulation
    /// thread while running (not while suspended); implementations must not block. Default: no-op, so the
    /// hook stays opt-in for implementers that don't surface records.
    /// </summary>
    void OnRecord(string text, long year) { }
}
