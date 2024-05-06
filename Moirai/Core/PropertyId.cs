public readonly struct PropertyId : IEquatable<PropertyId>
{
    public bool Equals(PropertyId other) => Id == other.Id && TypeId.Equals(other.TypeId);

    public override bool Equals(object? obj)
    {
        return obj is PropertyId other && Equals(other);
    }

    public override int GetHashCode() => HashCode.Combine(Id, TypeId);

    public static bool operator ==(PropertyId left, PropertyId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PropertyId left, PropertyId right)
    {
        return !left.Equals(right);
    }

    public static readonly PropertyId Null = new PropertyId();
    public bool IsValid => Id != 0;// && TypeId.IsValid;
    public readonly uint Id;
    public readonly EntityTypeId TypeId;
    public PropertyId(uint id, EntityTypeId entityTypeId)
    {
        Id = id;
        TypeId = entityTypeId;
    }

    public static explicit operator int(PropertyId p) => (int)p.Id;

    public override string ToString()
    {
        if (Database.Instance != null)
        {
            return $"p{Id}:{Database.Instance.GetPropertyName(this)}";
        }
        return $"p{Id}";
    }
}
