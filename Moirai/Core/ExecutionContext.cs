using System.Diagnostics;

namespace Moirai.Core;

public class ExecuteContext
{
    public long Year { get; internal set; }

    public readonly Database Database;

    // The active RNG. It is swapped to the firing event/trigger's own stream while that rule runs (see
    // UseStream / RunAction / RunTriggers), then restored — so each rule's randomness is independent.
    public Pcg32 Rnd;

    // Per-rule RNG streams: PCG offers 2^63 independent streams selected by the `sequence` argument.
    // Each event/trigger draws from a stream keyed by a stable hash of its name, so adding or
    // reordering one rule no longer reshuffles the draws of any other (the cause of the "add a system,
    // an unrelated test flips to zero" brittleness). Streams are cached so a rule's stream persists
    // across all its firings within a run.
    private ulong _baseSeed;
    private readonly Dictionary<ulong, Pcg32> _streams = new();

    private List<PropertyValue> _values = new();
    private List<EntityId> _pool = new();
    public int ValueOffset { get; set; }
    public int ValueCount => _values.Count;
    public PropertyValue LastValue => _values.LastOrDefault();

    public ExecuteContext(Database database, ulong seed)
    {
        Database = database;
        _baseSeed = seed;
        Rnd = new Pcg32(seed, 42);
    }

    /// <summary>The cached RNG stream for <paramref name="streamId"/>, created lazily from the base seed.</summary>
    public Pcg32 StreamFor(ulong streamId)
    {
        if (!_streams.TryGetValue(streamId, out var rng))
            _streams[streamId] = rng = new Pcg32(_baseSeed, streamId);
        return rng;
    }

    /// <summary>Switch <see cref="Rnd"/> to a rule's stream for the duration of a `using` block.</summary>
    public RngScope UseStream(ulong streamId)
    {
        var prev = Rnd;
        Rnd = StreamFor(streamId);
        return new RngScope(this, prev);
    }

    public readonly struct RngScope : IDisposable
    {
        private readonly ExecuteContext _ctx;
        private readonly Pcg32 _prev;
        public RngScope(ExecuteContext ctx, Pcg32 prev) { _ctx = ctx; _prev = prev; }
        public void Dispose() => _ctx.Rnd = _prev;
    }

    /// <summary>Reset all randomness to a fresh base seed (clears every per-rule stream).</summary>
    public void Reseed(ulong seed)
    {
        _baseSeed = seed;
        _streams.Clear();
        Rnd = new Pcg32(seed, seed);
    }

    public EntityId GetSingletonId(EntityTypeId type)
    {
        // Fast path for `singleton`-declared types (O(1)); scan as a fallback for plain types.
        if (Database.TryGetSingleton(type, out var cached))
            return cached;
        foreach (var entity in Database.Entities)
        {
            if (entity.Type == type)
            {
                return entity.Id;
            }
        }
        return default;
    }
    public bool GetSingleton(EntityTypeId type, out Entity value)
    {
        if (Database.TryGetSingleton(type, out var cached) && Database.TryGetEntity(cached, out value))
            return true;

        foreach (var entity in Database.Entities)
        {
            if (entity.Type == type)
            {
                value = entity;
                return true;
            }
        }
        value = default;
        return false;
    }


    public bool PickRandom(EntityTypeId entityTypeId, IValueSql value, int varIdx, out EntityId id)
    {
        return Database.PickRandom(entityTypeId, value, varIdx, out id);
    }

    public PropertyValue Argument(int idx)
    {
        if (idx == -1)
            throw new InvalidOperationException("Obsolete -1 var index allowed");
        return _values[idx + ValueOffset];
    }
    public void SetArgument(int argumentIndex, PropertyValue value)
    {
        while (_values.Count <= argumentIndex + ValueOffset)
            _values.Add(default);
        _values[argumentIndex + ValueOffset] = value;
    }
    public void ClearValueStack()
    {
        _values.RemoveRange(ValueOffset, _values.Count - ValueOffset);
    }

    /// <summary>
    /// Read a local variable slot of the current frame (slot is relative to <see cref="ValueOffset"/>).
    /// Used by the debugger to inspect <c>$vars</c>; returns false when the slot has not been written yet.
    /// </summary>
    public bool TryGetLocal(int slot, out PropertyValue value)
    {
        int i = slot + ValueOffset;
        if (i >= 0 && i < _values.Count)
        {
            value = _values[i];
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Read a value-stack entry by absolute index (i.e. a frame's <c>valueOffset + slot</c>).
    /// Used by the debugger to inspect locals of frames other than the current one.
    /// </summary>
    public bool TryGetValueAt(int absoluteIndex, out PropertyValue value)
    {
        if (absoluteIndex >= 0 && absoluteIndex < _values.Count)
        {
            value = _values[absoluteIndex];
            return true;
        }

        value = default;
        return false;
    }


    public Scope RunScope(bool setOffset)
    {
        return new Scope(this, _values.Count, ValueOffset, setOffset);
    }
    public void Assert(bool boolValue, string msg)
    {
        // Console.WriteLine(msg);
        if (!boolValue)
            throw new InvalidOperationException("assert failed: " + msg);
    }

    public struct Scope : IDisposable
    {
        private readonly ExecuteContext _executeContext;
        private readonly int _valuesCount;
        private readonly int _valueOffset;
        public Scope(ExecuteContext ctx, int valuesCount, int valueOffset, bool setOffset)
        {
            _executeContext = ctx;
            _valuesCount = valuesCount;
            _valueOffset = valueOffset;

            if(setOffset)
                Start();
        }
        public void Start() => _executeContext.ValueOffset = _valuesCount;
        public void Dispose()
        {
            _executeContext.ValueOffset = _valueOffset;
            _executeContext._values.RemoveRange(_valuesCount, _executeContext._values.Count - _valuesCount);

        }
    }

   
    public void PassYears(int years, bool offset) => PassYears(years, CancellationToken.None, null, offset);
    public void PassYears(long years, CancellationToken token, IProgress<int>? progress, bool offset)
    {
        Stopwatch sw = Stopwatch.StartNew();
        Database.ExecProfiler = Database.ProfilingEnabled ? new ExecutionProfiler() : null;
        Database.CurrentChangeset = new Changeset(-1, "time", Int64.MaxValue);
        var timeType = Database.GetEntityType("Time");
        var timeId = this.GetSingletonId(timeType.Id);
        var yearsProp = timeType.GetPropertyId("year");
        if (!Database.TryGetEntity(timeId, out var time))
            throw new NotImplementedException("missing Time entity");

        Year = time.GetProperty(yearsProp).IntValue;
        var howMany = offset ? years : (years - Year);
        for (int i = 0; i < howMany; i++)
        {
            if (token.IsCancellationRequested)
                return;
            //Console.WriteLine("\tTIME " + _year);
            Database.SetProperty(timeId, yearsProp, ++Year);
            progress?.Report(i);
            // Fire any deferred `schedule(...)` effects that have come due before this year's regular events,
            // so a state transition (e.g. a person turning Young) is visible to this year's events.
            Database.DrainScheduled(Year);
            foreach (var action in Database.Actions)
            {
                if (action.Filter == null || action.Skip)
                    continue;

                // Sample the frequency AND run the event on the event's own stream, so its scheduling
                // draws are decoupled from other events too (RunAction re-selects the same stream).
                using (UseStream(action.RngStreamId))
                {
                    int count = (int)action.Filter.Compute(Database.Ctx, Year);
                    for (int j = 0; j < count; j++)
                    {
                        Database.RunAction(action);
                    }
                }
            }
        }

        Console.WriteLine("PassYears " + years + " took " + sw.ElapsedMilliseconds + "ms");
        Profiler.Dump();

        if (Database.ExecProfiler != null)
        {
            Database.ExecProfiler.Years = howMany;
            Database.ExecProfiler.ElapsedTicks = sw.ElapsedTicks;
            Console.WriteLine(Database.ExecProfiler.Report());
        }
    }

    public Entity PrevEntity;
    internal PropertyValue GetPrevEntityProperty(PropertyId property)
    {
        if (!property.IsValid)
            return PrevEntity.Id;
        if (PrevEntity.Id.IsNull)
            return default;
        // throw new InvalidOperationException("Null prev entity access");
        return PrevEntity.GetProperty(property);
    }

    // tuple eid, eventId -> year
    public void Mark(EntityId eId, int eventIndex)
    {
        Database.Mark(eId, eventIndex);
    }

    public bool GetLastMarked(EntityId eId, int eventIndex, out long year)
    {
        return Database.GetLastMarked(eId, eventIndex, out year);
    }
}
