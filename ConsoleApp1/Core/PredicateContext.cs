using Pcg;

public class PredicateContext
{
    public readonly Database Database;
    public List<PropertyValue> Values = new();
    public Pcg32 Rnd;

    public PredicateContext(Database database)
    {
        Database = database;
        Rnd = new Pcg32(42, 42);
    }
    public long EntityId => Values[^1].IntValue;

    public bool Query(IPredicate? predicate, out EntityId value)
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

    public bool PickRandom(IPredicate predicate, out EntityId value)
    {
        FindAll(predicate, ref _pool);
        if (_pool.Count == 0)
        {
            value = default;
            return false;
        }
        value = _pool[(int)Rnd.GenerateNext((uint)_pool.Count)];
        return true;
    }
    public bool FindAll( IPredicate? predicate,ref List<EntityId> results)
    {
        results.Clear();
        if (predicate == null)
        {
            return true;
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
        
        return false;
    }
    public void PopArgument() => Values.RemoveAt(Values.Count - 1);
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
    public void PushArgument(long entity)
    {
        Values.Add(entity);
    }
    public PropertyValue GetValue(ComputedValue computedValue)
    {
        switch (computedValue.Type)
        {

            case ComputedValue.ComputedValueType.Value:
                return computedValue.Value;
            case ComputedValue.ComputedValueType.Path:
                var varValue = this.Argument(computedValue.Path.VariableIndex);
                if (!Database.TryGetEntity(varValue.IntValue, out var e))
                    return default;
                if (computedValue.Path.Property == null)
                    return varValue;

                return e.GetProperty(computedValue.Path.Property.Value);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}