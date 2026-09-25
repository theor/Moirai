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

    public IReadOnlyCollection<Property> Properties =>
        _properties == null! || (_offset == 0 && _count == _properties.Length)
            ? _properties
            : new ArraySegment<Property>(_properties, _offset, _count);

    // An entity's slots are _count properties from _offset in _properties: its own array, or a stretch of a
    // slab shared with other entities (see Slab), which is how a birth avoids an array of its own.
    private Property[] _properties;
    private int _offset;
    private int _count;

    // The slots themselves, indexed by PropertyId.Id, for the history to diff and copy without boxing.
    internal readonly Span<Property> RawProperties =>
        _properties == null ? default : new Span<Property>(_properties, _offset, _count);

    public Entity(EntityType type) : this()
    {
        Type = type.Id;
        _count = type.Properties.Count;
        _properties = new Property[_count];
    }

    internal Entity(EntityType type, Slab<Property> slab) : this()
    {
        Type = type.Id;
        _count = type.Properties.Count;
        (_properties, _offset) = slab.Take(_count);
    }

    private Entity(Entity other)
    {
        Id = other.Id;
        _type = other._type;
        _count = other._count;
        _properties = other.RawProperties.ToArray();
    }

    internal void Reset()
    {
        Id = default;
        _type = default;
        RawProperties.Clear();
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
            throw new NotImplementedException("Null property");
        if (_properties == null)
        {
            value = default;
            return false;
        }

        if (property.Id >= _count)
        {
            value = default;
            return false;
        }

        ref readonly var slot = ref _properties[_offset + property.Id];
        value = slot.Value;
        return slot.Id.IsValid;
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
        ref var p = ref _properties[_offset + propertyId.Id];
        var prev = p.Value;
        p.Id = propertyId;
        p.Value = value;
        return prev;
    }

    public Entity Clone()
    {
        return new Entity(this);
    }
}

/// <summary>
/// What the engine's storage has allocated on this thread: every chunk a <see cref="Moirai.Core.ChunkedList{T}"/>
/// or a <see cref="Slab{T}"/> takes, to the byte. Those are the allocations a growing world cannot avoid --
/// the world's own data -- so a measurement can subtract them and see whether anything else allocated.
/// </summary>
public static class StorageStats
{
    [ThreadStatic] public static long ChunkBytes;
}

/// <summary>
/// Hands out runs of slots from shared arrays of about 64 KB, so that storing something that lives as
/// long as the world -- a new entity's properties, the history's copy of them, a record's text -- takes a
/// stretch of an existing array instead of an array of its own. Nothing is ever given back.
/// </summary>
internal sealed class Slab<T>
{
    // Under the large object heap threshold for anything up to 32 bytes an item.
    private static readonly int Size = Math.Max(1024, 64 * 1024 / Math.Max(1, System.Runtime.CompilerServices.Unsafe.SizeOf<T>()));
    private T[] _chunk = System.Array.Empty<T>();
    private int _used;

    public (T[] Array, int Offset) Take(int count)
    {
        if (_used + count > _chunk.Length)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            _chunk = new T[Math.Max(Size, count)];
            StorageStats.ChunkBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            _used = 0;
        }

        var offset = _used;
        _used += count;
        return (_chunk, offset);
    }
}

public static class Profiler
{
    class PropData
    {
        public required EntityType Type;
        public PropertyDefinition Property;
        public int GetCount;
        public int SetCount;
    }

    static PropData[][]? Hits;
    static int[]? ValueHits;
    static (int, int)[]? HitsOfType;
    private static Database? _db;

    [Conditional("DEBUG")]
    public static void Get(PropertyId id)
    {
        if (Hits != null)
            Hits[id.TypeId.Id][(int)id.Id].GetCount++;
    }

    [Conditional("DEBUG")]
    public static void Set(PropertyId id)
    {
        if (Hits != null)
            Hits[id.TypeId.Id][(int)id.Id].SetCount++;
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
        if(Hits!= null)
            foreach (var property in Hits.SelectMany(x => x)
                         .OrderByDescending(x => x.GetCount))
            {
                Debug.WriteLine(
                    $"  {property.Type.Name}.{property.Property.Name}: get {property.GetCount} / set {property.SetCount}");
            }

        Debug.WriteLine("* VALUEHITS");
        if(ValueHits != null)
            foreach (var type in Enum.GetValues<PropertyValue.ValueBaseType>())
            {
                Debug.WriteLine($"  {type,10}: {ValueHits[(int)type]}");
            }

        Debug.WriteLine("* EVENTS");
        if (HitsOfType != null)
            for (var index = 0; index < HitsOfType.Length; index++)
            {
                var (total, success) = HitsOfType[index];
                Debug.WriteLine($"  {_db!.Types[index].Name,10}: {100 * success / (float)total}% {success} / {total}");
            }

        Debug.WriteLine(
            $"Events: {Database.EventAttemptSuccess} / {Database.EventAttemptCount} = {100f * Database.EventAttemptSuccess / Database.EventAttemptCount}%");
    }
}
