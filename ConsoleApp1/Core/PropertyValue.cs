using System.Xml;

public struct EntityId
{
    public static readonly EntityId Null = default;
    public readonly long Id;
    public EntityId(long id)
    {
        Id = id;
    }
    public override string ToString()
    {
        return $"#{Id}";
    }
}

public struct PropertyValue : IEquatable<PropertyValue>
{
    public string? Value;
    public long IntValue;

    public enum ValueType
    {
        String,
        EntityId,
        Number,
        Bool,
    }

    public ValueType Type;
    public static implicit operator PropertyValue(string s) => new PropertyValue
    {
        Value = s,
        IntValue = Int32.MinValue,
        Type = ValueType.String,
    };
    public static implicit operator PropertyValue(EntityId i) => new PropertyValue
    {
        Value = null,
        IntValue = i.Id,
        Type = ValueType.EntityId,
    };
    public static implicit operator PropertyValue(long i) => new PropertyValue
    {
        Value = null,
        IntValue = i,
        Type = ValueType.Number,
    };
    public static implicit operator PropertyValue(bool b) => new PropertyValue
    {
        Value = null,
        IntValue = b ? 1 : 0,
        Type = ValueType.Bool,
    };
    public bool BoolValue => IntValue != 0;

    public bool Equals(PropertyValue other)
    {
        return Value == other.Value && IntValue == other.IntValue;
    }
    public override bool Equals(object? obj)
    {
        return obj is PropertyValue other && Equals(other);
    }
    public override int GetHashCode()
    {
        return HashCode.Combine(Value, IntValue);
    }
    public static bool operator ==(PropertyValue left, PropertyValue right)
    {
        return left.Equals(right);
    }
    public static bool operator !=(PropertyValue left, PropertyValue right)
    {
        return !left.Equals(right);
    }
}