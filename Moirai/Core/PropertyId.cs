public readonly struct PropertyId : IEquatable<PropertyId>
{
    public bool Equals(PropertyId other)
    {
        return Id == other.Id;
    }
    public override bool Equals(object? obj)
    {
        return obj is PropertyId other && Equals(other);
    }
    public override int GetHashCode()
    {
        return (int)Id;
    }
    public static bool operator ==(PropertyId left, PropertyId right)
    {
        return left.Id == right.Id;
    }
    public static bool operator !=(PropertyId left, PropertyId right)
    {
        return left.Id != right.Id;
    }
    public static readonly PropertyId Null = new PropertyId();
    public bool IsValid => Id != 0;
    public readonly uint Id;
    public readonly EntityTypeId TypeId;
    public PropertyId(uint id, EntityTypeId typeId)
    {
        Id = id;
        TypeId = typeId;
    }
    public override string ToString()
    {
        // if (Database.Instance != null)
        // {
        //     return $"p{Id}:{Database.Instance.Properties[(int)Id].Name}";
        // }
        return $"p{Id}";
    }
}
