using System.Threading;

namespace Moirai.Core;

/// <summary>
/// The engine-side controller for a step-through debug session. Implements
/// <see cref="IDebugHook"/> (so it observes execution on the simulation thread) and exposes a
/// protocol-agnostic API (<see cref="SetBreakpoints"/>, <see cref="Continue"/>,
/// <see cref="StepOver"/>, …, <see cref="GetStack"/>, <see cref="GetVariables"/>) that a Debug
/// Adapter binds to. Kept free of any DAP/transport dependency so it can be unit-tested directly.
///
/// Threading: the simulation runs on one worker thread. When a breakpoint or step lands,
/// <see cref="OnStatement"/> raises <see cref="Stopped"/> and then BLOCKS that thread on a gate
/// until a resume call (Continue/Step*) is made from the protocol thread. While blocked the world
/// is frozen, so the protocol thread can safely read frames/variables.
/// </summary>
public sealed class DebugSession : IDebugHook
{
    public enum StopReason { Entry, Breakpoint, Step, Pause }

    public sealed class StackFrame
    {
        public int Id;
        public string Name = "";
        public DebugFrameKind Kind;
        public int Line;       // 1-based for DAP
        public int Column;     // 1-based for DAP
        internal DebugScope? Scope;
        internal int ValueOffset;
        internal SourceSpan Source;
    }

    public sealed class Variable
    {
        public string Name = "";
        public string Value = "";
        /// <summary>0 for a leaf; otherwise a handle the client passes back to expand children (e.g. an entity's properties).</summary>
        public int VariablesReference;
    }

    public readonly record struct StopInfo(StopReason Reason, int Line, int Column);

    /// <summary>Raised on the simulation thread when execution suspends. The handler must not block.</summary>
    public event Action<StopInfo>? Stopped;
    /// <summary>Raised when execution resumes after a stop.</summary>
    public event Action? Continued;

    // ---- breakpoints -------------------------------------------------------
    // Breakpoints are matched by 1-based line only (single-story server). Path is retained for
    // a future multi-file world but not currently part of the match.
    private readonly object _bpLock = new();
    private HashSet<int> _breakpointLines = new();

    /// <summary>Replace the breakpoints for <paramref name="path"/>. Returns the accepted (1-based) lines.</summary>
    public int[] SetBreakpoints(string path, IEnumerable<int> lines)
    {
        lock (_bpLock)
        {
            _breakpointLines = new HashSet<int>(lines);
            return _breakpointLines.ToArray();
        }
    }

    private bool IsBreakpoint(int line1Based)
    {
        lock (_bpLock)
            return _breakpointLines.Contains(line1Based);
    }

    // ---- run/step state ----------------------------------------------------
    private enum Mode { Continue, StepOver, StepIn, StepOut }

    private readonly ManualResetEventSlim _gate = new(false);
    private readonly object _stateLock = new();
    private readonly List<StackFrame> _frames = new();

    private Mode _mode = Mode.Continue;
    private volatile bool _pauseRequested;
    private volatile bool _stopped;
    private volatile bool _terminated;
    private int _stepBaseDepth;
    private bool _stopOnEntry;

    // Captured at a stop so the protocol thread can read locals while the sim thread is frozen.
    private ExecuteContext? _stoppedCtx;
    private StackFrame[] _stoppedFrames = Array.Empty<StackFrame>();

    // variablesReference handles, valid only for the current stop. reference == index + 1
    // (0 means "no children"). A handle resolves to a frame's locals, an entity to expand,
    // or the top-level world overview.
    private enum HandleKind { FrameLocals, Entity, World }
    private readonly List<(HandleKind Kind, int FrameId, EntityId Entity)> _handles = new();

    public bool IsStopped => _stopped;
    public long CurrentYear => _stoppedCtx?.Year ?? 0;

    /// <summary>Stop at the very first executed statement (set before the run starts).</summary>
    public void SetStopOnEntry(bool value) => _stopOnEntry = value;

    // ---- IDebugHook (simulation thread) ------------------------------------
    public void OnEnterFrame(DebugFrameKind kind, string name, DebugScope? scope, int valueOffset)
    {
        lock (_stateLock)
            _frames.Add(new StackFrame { Name = name, Kind = kind, Scope = scope, ValueOffset = valueOffset });
    }

    public void OnExitFrame()
    {
        lock (_stateLock)
            if (_frames.Count > 0)
                _frames.RemoveAt(_frames.Count - 1);
    }

    public void OnStatement(IInstruction instruction, ExecuteContext ctx)
    {
        if (_terminated)
            return;

        int depth;
        lock (_stateLock)
        {
            if (_frames.Count > 0)
                _frames[^1].Source = instruction.Source;
            depth = _frames.Count;
        }

        int line1 = instruction.Source.IsValid ? instruction.Source.StartLine + 1 : -1;
        int col1 = instruction.Source.IsValid ? instruction.Source.StartColumn + 1 : 0;

        var reason = ShouldStop(line1, depth);
        if (reason == null)
            return;

        Suspend(reason.Value, ctx, line1, col1);
    }

    private StopReason? ShouldStop(int line1, int depth)
    {
        if (_stopOnEntry)
        {
            _stopOnEntry = false;
            return StopReason.Entry;
        }

        if (_pauseRequested)
        {
            _pauseRequested = false;
            return StopReason.Pause;
        }

        if (line1 > 0 && IsBreakpoint(line1))
            return StopReason.Breakpoint;

        switch (_mode)
        {
            case Mode.StepIn:
                return StopReason.Step;
            case Mode.StepOver:
                if (depth <= _stepBaseDepth) return StopReason.Step;
                break;
            case Mode.StepOut:
                if (depth < _stepBaseDepth) return StopReason.Step;
                break;
        }

        return null;
    }

    private void Suspend(StopReason reason, ExecuteContext ctx, int line1, int col1)
    {
        lock (_stateLock)
        {
            _stoppedCtx = ctx;
            _stoppedFrames = SnapshotFramesLocked();
            _handles.Clear();   // references are per-stop
            _stopped = true;
        }

        _gate.Reset();
        Stopped?.Invoke(new StopInfo(reason, line1, col1));

        _gate.Wait();   // block the simulation thread until a resume call

        lock (_stateLock)
        {
            _stopped = false;
            _stoppedCtx = null;
            _stoppedFrames = Array.Empty<StackFrame>();
        }

        Continued?.Invoke();
    }

    // Build the DAP-facing frame list (innermost first), assigning ids.
    private StackFrame[] SnapshotFramesLocked()
    {
        var result = new StackFrame[_frames.Count];
        for (int i = 0; i < _frames.Count; i++)
        {
            // DAP convention: frame 0 is the top (innermost) of the stack.
            var src = _frames[_frames.Count - 1 - i];
            result[i] = new StackFrame
            {
                Id = i,
                Name = src.Name,
                Kind = src.Kind,
                Line = src.Source.IsValid ? src.Source.StartLine + 1 : 0,
                Column = src.Source.IsValid ? src.Source.StartColumn + 1 : 0,
                Scope = src.Scope,
                ValueOffset = src.ValueOffset,
                Source = src.Source,
            };
        }

        return result;
    }

    // ---- protocol-facing queries (valid only while stopped) ----------------
    public StackFrame[] GetStack()
    {
        lock (_stateLock)
            return _stoppedFrames;
    }

    /// <summary>Allocate (per-stop) a variablesReference for a frame's Locals scope.</summary>
    public int GetScopeReference(int frameId)
    {
        lock (_stateLock)
        {
            _handles.Add((HandleKind.FrameLocals, frameId, default));
            return _handles.Count;
        }
    }

    /// <summary>Allocate (per-stop) a variablesReference for the top-level World overview scope.</summary>
    public int GetWorldReference()
    {
        lock (_stateLock)
        {
            _handles.Add((HandleKind.World, 0, default));
            return _handles.Count;
        }
    }

    /// <summary>
    /// Resolve a variablesReference to its children: a frame's locals, or — for an entity-typed
    /// variable — that entity's properties. Entity-valued children get their own reference so the
    /// watch/variables tree can be expanded recursively. Valid only while stopped.
    /// </summary>
    public Variable[] GetVariablesByReference(int reference)
    {
        lock (_stateLock)
        {
            var ctx = _stoppedCtx;
            if (ctx == null || reference <= 0 || reference > _handles.Count)
                return Array.Empty<Variable>();

            var handle = _handles[reference - 1];
            return handle.Kind switch
            {
                HandleKind.Entity => EntityMembersLocked(ctx, handle.Entity),
                HandleKind.World => WorldMembersLocked(ctx),
                _ => FrameLocalsLocked(ctx, handle.FrameId),
            };
        }
    }

    /// <summary>Back-compat convenience: a frame's locals (used by tests).</summary>
    public Variable[] GetVariables(int frameId) =>
        GetVariablesByReference(GetScopeReference(frameId));

    // --- the following run under _stateLock (sim thread frozen, so reads are safe) ---

    private Variable[] FrameLocalsLocked(ExecuteContext ctx, int frameId)
    {
        if (frameId < 0 || frameId >= _stoppedFrames.Length)
            return Array.Empty<Variable>();
        var frame = _stoppedFrames[frameId];
        if (frame.Scope == null || !frame.Source.IsValid)
            return Array.Empty<Variable>();

        var result = new List<Variable>();
        var seen = new HashSet<int>();
        foreach (var (slot, name) in frame.Scope.Innermost(frame.Source).VisibleVariables())
        {
            if (!seen.Add(slot))
                continue;
            if (ctx.TryGetValueAt(frame.ValueOffset + slot, out var value))
                result.Add(MakeVariableLocked(ctx, name, value));
        }

        return result.ToArray();
    }

    private Variable[] EntityMembersLocked(ExecuteContext ctx, EntityId id)
    {
        if (!ctx.Database.TryGetEntity(id, out var entity))
            return Array.Empty<Variable>();

        var result = new List<Variable>();
        foreach (var p in entity.Properties)
        {
            if (!p.Id.IsValid)
                continue;
            result.Add(MakeVariableLocked(ctx, ctx.Database.GetPropertyName(p.Id), p.Value));
        }

        return result.ToArray();
    }

    // The top-level World overview: current year, total entity count, and per-type counts.
    // Singleton types (e.g. Time) are shown as expandable entities so their props are reachable.
    private Variable[] WorldMembersLocked(ExecuteContext ctx)
    {
        var db = ctx.Database;
        var result = new List<Variable> { new() { Name = "year", Value = ctx.Year.ToString() } };

        var counts = new Dictionary<uint, int>();
        int total = 0;
        foreach (var e in db.Entities)
        {
            counts.TryGetValue(e.Type.Id, out var c);
            counts[e.Type.Id] = c + 1;
            total++;
        }

        result.Add(new Variable { Name = "entities", Value = total.ToString() });

        foreach (var t in db.Types)
        {
            if (t.Id.Id == 0)
                continue; // the default/placeholder type
            if (db.TryGetSingleton(t.Id, out var sid) && !sid.IsNull)
            {
                _handles.Add((HandleKind.Entity, 0, sid));
                result.Add(new Variable { Name = t.Name, Value = DescribeEntity(db, sid), VariablesReference = _handles.Count });
            }
            else
            {
                counts.TryGetValue(t.Id.Id, out var c);
                result.Add(new Variable { Name = t.Name, Value = c.ToString() });
            }
        }

        return result.ToArray();
    }

    // Build a Variable; entity-typed (non-null ref) values become expandable.
    private Variable MakeVariableLocked(ExecuteContext ctx, string name, PropertyValue value)
    {
        var db = ctx.Database;
        if (value.Type.BaseType == PropertyValue.ValueBaseType.Ref && !value.Id.IsNull)
        {
            _handles.Add((HandleKind.Entity, 0, value.Id));
            return new Variable { Name = name, Value = DescribeEntity(db, value.Id), VariablesReference = _handles.Count };
        }

        return new Variable { Name = name, Value = db.Printer.Print(value) };
    }

    private static string DescribeEntity(Database db, EntityId id)
    {
        if (!db.TryGetEntity(id, out var e))
            return id.ToString();
        var label = db.GetEntityType(e.Type).Name + " " + id;
        if (db.GetProperty(id, Database.PropName, out var n) && !string.IsNullOrEmpty(n.Value))
            label += " '" + n.Value + "'";
        return label;
    }

    // ---- resume controls (protocol thread) ---------------------------------
    public void Continue() => Resume(Mode.Continue);
    public void StepOver() => Resume(Mode.StepOver);
    public void StepIn() => Resume(Mode.StepIn);
    public void StepOut() => Resume(Mode.StepOut);

    private void Resume(Mode mode)
    {
        lock (_stateLock)
        {
            _mode = mode;
            // Step modes are relative to the depth we are stopped at.
            _stepBaseDepth = _frames.Count;
        }

        _gate.Set();
    }

    /// <summary>Request a stop at the next statement (no effect if already stopped).</summary>
    public void Pause() => _pauseRequested = true;

    /// <summary>End the session: release any blocked simulation thread and stop intercepting.</summary>
    public void Terminate()
    {
        _terminated = true;
        _gate.Set();
    }
}
