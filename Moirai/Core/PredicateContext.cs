using System.Diagnostics;
using Moirai;
using Moirai.Core;

public class PredicateContext
{
    public long Year { get; private set; }

    public readonly Database Database;
    public Pcg32 Rnd;
    private List<PropertyValue> _values = new();
    private List<EntityId> _pool = new();
    public int ValueOffset { get; set; }
    public int ValueCount => _values.Count;
    public PropertyValue LastValue => _values.Last();

    public PredicateContext(Database database, ulong seed)
    {
        Database = database;
        Rnd = new Pcg32(seed, 42);
    }

    public EntityId GetSingletonId(EntityTypeId type)
    {
        foreach (var entity in Database.Entities)
        {
            if (entity.GetProperty(Database.PropType).TypeId == type)
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
            if (entity.GetProperty(Database.PropType).TypeId == type)
            {
                value = entity;
                return true;
            }
        }
        value = default;
        return false;
    }


    public bool PickRandom(IValue value, out EntityId id)
    {
        // Console.ForegroundColor = ConsoleColor.Blue;
        // Console.WriteLine($"PICK {Database.Printer.Print(value)}  VAL COUNT {ValueCount} OFFSET {ValueOffset}");
        // Console.ResetColor();
        return Database.PickRandom(value, out id);
        // Database.FindAll(value, ref _pool);
        // if (_pool.Count == 0)
        // {
        //     id = default;
        //     return false;
        // }
        // id = _pool[(int)Rnd.GenerateNext((uint)_pool.Count)];
        // return true;
    }
    public bool _FindAll(IValue? predicate, ref List<EntityId> results)
    {
        results.Clear();
        if (predicate == null)
        {
            return false;
        }

        var (where,joins) = predicate.ToSql(this);
        Debug.WriteLine(where,joins);
        
        // if (predicate.HasTypeFilter(out var typeFilter))
        // {
        //     var ids = Database.PerTypeIndices[(int)typeFilter.Id];
        //     foreach (var id in ids)
        //     {
        //         PushArgument(id);
        //         var isTrue = predicate.IsTrue(this);
        //         if (isTrue)
        //             results.Add(id);
        //         PopArgument();
        //
        //     }
        //     return true;
        // }
        foreach (var entity in Database.Entities)
        {
            PushArgument(entity.Id);
            // Database.Printer.PrintEntity(entity);
            var isTrue = predicate.IsTrue(this);
            // Console.ForegroundColor = isTrue ? ConsoleColor.DarkGreen : ConsoleColor.DarkRed;
            // Console.WriteLine($"  TEST #{entity.Id} {isTrue}");
            // Console.ResetColor();
            if (isTrue)
            {
                results.Add(entity.Id);
            }
            else
            {
            }
            // Console.ResetColor();
            PopArgument();
        }

        return true;
    }
    public int PopArgument()
    {
        _values.RemoveAt(_values.Count - 1);
        return _values.Count - ValueOffset;
    }
    public PropertyValue Argument(int idx)
    {
        if (idx == -1)
            return _values[_values.Count - 1];
        return _values[idx + ValueOffset];
    }
    public void SetArgument(int argumentIndex, PropertyValue value)
    {
        while (_values.Count <= argumentIndex + ValueOffset)
            _values.Add(default);
        _values[argumentIndex + ValueOffset] = value;
    }
    public int PushArgument(EntityId entity)
    {
        int count = _values.Count;
        _values.Add(entity);
        return count;
    }
    public void ClearValueStack()
    {
        _values.RemoveRange(ValueOffset, _values.Count - ValueOffset);
    }


    public Scope RunScope()
    {
        return new Scope(this, _values.Count, ValueOffset);
    }
    public void Assert(bool boolValue, string msg)
    {
        // Console.WriteLine(msg);
        if (!boolValue)
            throw new InvalidOperationException("assert failed: " + msg);
    }

    public struct Scope : IDisposable
    {
        private readonly PredicateContext _predicateContext;
        private readonly int _valuesCount;
        private readonly int _valueOffset;
        public Scope(PredicateContext ctx, int valuesCount, int valueOffset)
        {
            _predicateContext = ctx;
            _valuesCount = valuesCount;
            _valueOffset = valueOffset;

            ctx.ValueOffset = _valuesCount;
        }
        public void Dispose()
        {
            _predicateContext.ValueOffset = _valueOffset;
            _predicateContext._values.RemoveRange(_valuesCount, _predicateContext._values.Count - _valuesCount);

        }
    }

   
    public void PassYears(int years, bool offset) => PassYears(years, CancellationToken.None, null, offset);
    public void PassYears(long years, CancellationToken token, IProgress<int>? progress, bool offset)
    {
        Database.CurrentChangeset = new Changeset(-1, "time", Int64.MaxValue, Array.Empty<CategoryId>());
        var timeType = Database.GetEntityType("Time");
        var timeId = this.GetSingletonId(timeType.Id);
        var yearsProp = Database.GetPropertyId("Time", "year");
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

                int count = (int)action.Filter.Compute(Database.Ctx).IntValue;
                for (int j = 0; j < count; j++)
                {
                    Database.RunAction(action);
                }
            }
        }
        
        Profiler.Dump();
    }

    // public void TagEntity(EntityId id, TagId tagId)
    // {
    //     Database.CurrentChangeset.Changes.Add(Change.AddTag(id, tagId));
    // }
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
