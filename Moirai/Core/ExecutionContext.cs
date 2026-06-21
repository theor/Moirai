using System.Diagnostics;

namespace Moirai.Core;

public class ExecuteContext
{
    public long Year { get; internal set; }

    public readonly Database Database;
    public Pcg32 Rnd;
    private List<PropertyValue> _values = new();
    private List<EntityId> _pool = new();
    public int ValueOffset { get; set; }
    public int ValueCount => _values.Count;
    public PropertyValue LastValue => _values.LastOrDefault();

    public ExecuteContext(Database database, ulong seed)
    {
        Database = database;
        Rnd = new Pcg32(seed, 42);
    }

    /// <summary>
    /// Bound parameter values collected while compiling a single query's predicate to SQL.
    /// <see cref="Database.PickRandom"/>/<see cref="Database.FindAll"/> clear this before calling
    /// <c>ToSql</c>, then bind these values onto the (shape-keyed, cached) prepared statement.
    /// Leaf SQL nodes append via <see cref="AddSqlParameter"/>.
    /// </summary>
    public readonly List<PropertyValue> SqlParameters = new();

    // Interned "$p0".."$pN" placeholders so AddSqlParameter doesn't allocate a fresh string per
    // parameter per query (and so they match the prepared command's parameter names exactly).
    private static readonly string[] PlaceholderNames =
        Enumerable.Range(0, 64).Select(i => "$p" + i).ToArray();

    /// <summary>
    /// Records <paramref name="v"/> as a bound parameter and returns its placeholder (e.g. <c>$p0</c>),
    /// so two queries that differ only in runtime values share one cached/prepared statement.
    /// Null values are returned inline as the literal <c>null</c> (not bound) so that the
    /// equals/not-equals → IS [NOT] NULL rewrite keeps working.
    /// </summary>
    public string AddSqlParameter(PropertyValue v)
    {
        if (v.Type.BaseType == PropertyValue.ValueBaseType.None)
            return "null";
        SqlParameters.Add(v);
        int i = SqlParameters.Count - 1;
        return i < PlaceholderNames.Length ? PlaceholderNames[i] : "$p" + i;
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

                int count = (int)action.Filter.Compute(Database.Ctx, Year);
                for (int j = 0; j < count; j++)
                {
                    Database.RunAction(action);
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
