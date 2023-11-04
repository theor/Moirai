using Pcg;

public class PredicateContext
{
    public readonly Database Database;
    public List<PropertyValue> Values = new();
    public Pcg32 Rnd;

    public PredicateContext(Database database, ulong seed)
    {
        Database = database;
        Rnd = new Pcg32(seed, 42);
    }
    public long EntityId => Values[^1].IntValue;

    public bool Query(IValue? predicate, out EntityId value)
    {
        if (predicate == null)
        {
            value = default;
            return true;
        }
        var iterationIdx = Values.Count;
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
        var iterationIdx = Values.Count;
        foreach (var entity in Database.Entities)
        {
            SetArgument(iterationIdx, entity.Id);
            if (predicate.IsTrue(this))
            {
                results.Add(entity.Id);
            }
            PopArgument();
        }
        
        return true;
    }
    public int PopArgument()
    {
        Values.RemoveAt(Values.Count - 1);
        return Values.Count;
    }
    public PropertyValue Argument(int idx)
    {
        return Values[idx];
    }
    public void SetArgument(int argumentIndex, PropertyValue value)
    {
        while (Values.Count <= argumentIndex)
            Values.Add(default);
        Values[argumentIndex] = value;
    }
    public int PushArgument(EntityId entity)
    {
        int count = Values.Count;
        Values.Add(entity);
        return count;
    }
    
    public void Assert(bool boolValue, string msg)
    {
        if (!boolValue)
            throw new InvalidOperationException("assert failed: " + msg);
    }
}