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
    public List<Property>? Properties;
    public Entity(params Property[] properties) : this()
    {
        Properties ??= new();
        Properties.AddRange(properties);
    }
    public bool TryGetProperty(PropertyId property, out PropertyValue value)
    {
        Profiler.Get(property);
        if (property == Database.PropId)
        {
            value = Id;
            return true;
        }
        if (property == Database.PropType)
        {
            value = Type;
            return true;
        }
        if(!property.IsValid)
            throw new System.NotImplementedException("Null property");
        if (Properties == null)
        {
            value = default;
            return false;
        }
        foreach (var p in Properties)
        {
            if (p.Id == property)
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
    public PropertyValue GetProperty(PropertyId property)
    {
        TryGetProperty(property, out var val);
        return val;
    }
}

public static class Profiler
{
    class PropData
    {
        public int Get, Set;
    }

    static PropData[]? Hits = null;
    static int[]? ValueHits = null;
    private static Database _db;
    [Conditional("DEBUG")]
    public static void Get(PropertyId id)
    {
        if(Hits != null)
        Hits[(int)id.Id].Get++;
    }
    [Conditional("DEBUG")]
    public static void Set(PropertyId id)
    {
        if(Hits != null)
        Hits[(int)id.Id].Set++;
    }
    [Conditional("DEBUG")]
    public static void Init(Database database)
    {
        _db = database;
        Hits = new PropData[database.Properties.Count];
        for (var index = 0; index < Hits.Length; index++)
        {
             Hits[index] = new();
        }
        ValueHits = new int[Enum.GetValues<PropertyValue.ValueBaseType>().Length];
    }
    [Conditional("DEBUG")]
    public static void Value(PropertyValue.ValueBaseType t)
    {
        if(ValueHits != null)
        ValueHits[(int)t]++;
    }
    [Conditional("DEBUG")]
    public static void Dump()
    {
        foreach (var property in _db.Properties.Select(p => (p, Hits[p.Id])).OrderByDescending(x => x.Item2.Get))
        {
            Debug.WriteLine($"{property.p.Name ?? ""}: get {property.Item2.Get} / set {property.Item2.Set}");
        }
        foreach (var type in Enum.GetValues<PropertyValue.ValueBaseType>())
        {
            Debug.WriteLine($"{type,10}: {ValueHits[(int)type]}");

        }
    }
}