using System.Diagnostics;

public struct Entity
{
    public EntityId Id;

    public EntityTypeId Type
    {
        get => _type.TypeId;
        set => _type = value;
    }

    public PropertyValue _type;
    public IReadOnlyCollection<Property> Properties => _properties;
    private Property[] _properties;

    public Entity(EntityType type) : this()
    {
        Type = type.Id;
        _properties = new Property[type.Properties.Count];
    }

    internal void Reset()
    {
        Id = default;
        _type = default;
        for (int i = 0; i < _properties.Length; i++)
        {
            _properties[i] = default;
        }
    }

    public readonly bool TryGetProperty(PropertyId property, out PropertyValue value)
    {
        Profiler.Get(property);
        if (property == Database.PropId)
        {
            value = Id;
            return true;
        }

        if (property == Database.PropType)
        {
            value = _type;
            return true;
        }

        if (!property.IsValid)
            throw new System.NotImplementedException("Null property");
        if (_properties == null)
        {
            value = default;
            return false;
        }

        if (property.Id >= _properties.Length)
        {
            value = default;
            return false;
        }

        value = _properties[property.Id].Value;
        return _properties[property.Id].Id.IsValid;
    }

    public readonly PropertyValue GetProperty(PropertyId property)
    {
        TryGetProperty(property, out var val);
        return val;
    }

    public PropertyValue SetProperty(PropertyId propertyId, PropertyValue value)
    {
        if (propertyId.TypeId.Id != Type.Id && propertyId.TypeId.Id != 0)
            throw new InvalidOperationException(
                $"Cannot set property {propertyId.TypeId.ToString()}.{propertyId} on entity {Id} of type {Type}");
        ref var p = ref _properties[propertyId.Id];
        var prev = p.Value;
        p.Id = propertyId;
        p.Value = value;
        return prev;
    }
}

public static class Profiler
{
    class PropData
    {
        public EntityType Type;
        public PropertyDefinition Property;
        public int Get, Set;
    }

    static PropData[][]? Hits = null;
    static int[]? ValueHits = null;
    static (int, int)[]? HitsOfType = null;
    private static Database _db;

    [Conditional("DEBUG")]
    public static void Get(PropertyId id)
    {
        if (Hits != null)
            Hits[id.TypeId.Id][(int)id.Id].Get++;
    }

    [Conditional("DEBUG")]
    public static void Set(PropertyId id)
    {
        if (Hits != null)
            Hits[id.TypeId.Id][(int)id.Id].Set++;
    }

    [Conditional("DEBUG")]
    public static void Init(Database database)
    {
        _db = database;
        Hits = new PropData[database.Types.Count][];
        for (var index = 0; index < Hits.Length; index++)
        {
            Hits[index] = new PropData[database.Types[index].Properties.Count];
            for (int i = 0; i < database.Types[index].Properties.Count; i++)
            {
                Hits[index][i] = new()
                {
                    Type = database.Types[index],
                    Property = database.Types[index].Properties[i],
                };
            }
        }

        ValueHits = new int[Enum.GetValues<PropertyValue.ValueBaseType>().Length];
        HitsOfType = new (int, int)[database.Types.Count];
    }

    [Conditional("DEBUG")]
    public static void Value(PropertyValue.ValueBaseType t)
    {
        if (ValueHits != null)
            ValueHits[(int)t]++;
    }

    public static void HitOfType(EntityTypeId t, bool success)
    {
        if (HitsOfType != null)
        {
            ref var h = ref HitsOfType[(int)t.Id];
            h.Item1++;
            if (success)
                h.Item2++;
        }
    }

    [Conditional("DEBUG")]
    public static void Dump()
    {
        Debug.WriteLine("* HITS");
        foreach (var property in Hits.SelectMany(x => x)
                     .OrderByDescending(x => x.Get))
        {
            Debug.WriteLine(
                $"  {property.Type.Name ?? ""}.{property.Property.Name}: get {property.Get} / set {property.Set}");
        }

        Debug.WriteLine("* VALUEHITS");
        foreach (var type in Enum.GetValues<PropertyValue.ValueBaseType>())
        {
            Debug.WriteLine($"  {type,10}: {ValueHits[(int)type]}");
        }

        Debug.WriteLine("* EVENTS");
        for (var index = 0; index < HitsOfType.Length; index++)
        {
            var (total, success) = HitsOfType[index];
            Debug.WriteLine($"  {_db.Types[index].Name,10}: {100 * success / (float)total}% {success} / {total}");
        }

        Debug.WriteLine(
            $"Events: {Database.EventAttemptSuccess} / {Database.EventAttemptCount} = {100f * Database.EventAttemptSuccess / Database.EventAttemptCount}%");
    }
}
