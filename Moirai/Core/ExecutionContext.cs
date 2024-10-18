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

    public EntityId GetSingletonId(EntityTypeId type)
    {
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


    public bool PickRandom(EntityTypeId entityTypeId, IValueSql value, out EntityId id)
    {
        return Database.PickRandom(entityTypeId, value, out id);
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
