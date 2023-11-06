using Pcg;

public class PredicateContext
{
    public readonly Database Database;
    private List<PropertyValue> _values = new();
    public Pcg32 Rnd;

    public PredicateContext(Database database, ulong seed)
    {
        Database = database;
        Rnd = new Pcg32(seed, 42);
    }
    public long EntityId => _values[^1].IntValue;
    public int ValueOffset { get; set; }
    public int ValueCount => _values.Count;
    public PropertyValue LastValue => _values.Last();

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
    public bool Query(IValue? predicate, out EntityId value)
    {
        if (predicate == null)
        {
            value = default;
            return true;
        }
        var iterationIdx = _values.Count;
        foreach (var entity in Database.Entities)
        {
            SetArgument(iterationIdx, entity.Id);
            if (predicate.IsTrue(this))
            {
                PopArgument();
                value = entity.Id;
                return true;
            }
        }
        PopArgument();
        value = default;
        return false;
    }
    private List<EntityId> _pool = new();

    public bool PickRandom(IValue value, out EntityId id)
    {
        // Console.ForegroundColor = ConsoleColor.Blue;
        // Console.WriteLine($"PICK {Database.Printer.Print(value)}  VAL COUNT {ValueCount} OFFSET {ValueOffset}");
        // Console.ResetColor(); 
        FindAll(value, ref _pool);
        if (_pool.Count == 0)
        {
            id = default;
            return false;
        }
        id = _pool[(int)Rnd.GenerateNext((uint)_pool.Count)];
        return true;
    }
    public bool FindAll( IValue? predicate,ref List<EntityId> results)
    {
        results.Clear();
        if (predicate == null)
        {
            return false;
        }

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
            }else{
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
        return _values[idx + ValueOffset];
    }
    public void SetArgument(int argumentIndex, PropertyValue value)
    {
        // TODO 
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
    
    public void Assert(bool boolValue, string msg)
    {
        if (!boolValue)
            throw new InvalidOperationException("assert failed: " + msg);
    }
    public void ClearValueStack()
    {
       _values.RemoveRange(ValueOffset, _values.Count - ValueOffset);
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

    public Scope RunScope()
    {
        return new Scope(this, _values.Count, ValueOffset);
    }
}