using System.Xml;

public struct EntityId
{
    public static readonly EntityId Null = default;
    public readonly bool IsNull => Id == 0;
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
    
    public static readonly ValueType TypeString = new ValueType(ValueBaseType.String, 0);
    public static readonly ValueType TypeRef = new ValueType(ValueBaseType.Ref, 0);
    public static readonly ValueType TypeNumber = new ValueType(ValueBaseType.Number, 0);
    public static readonly ValueType TypeBool = new ValueType(ValueBaseType.Bool, 0);
    public static readonly ValueType TypeEntityType = new ValueType(ValueBaseType.EntityType, 0);
    public static ValueType TypeEnum(ushort index) => new ValueType(ValueBaseType.Enum, index);

    public readonly struct ValueType : IEquatable<ValueType>
    {
        public readonly ValueBaseType BaseType;
        public readonly ushort Index;
        public ValueType(ValueBaseType baseType, ushort index)
        {
            BaseType = baseType;
            if(baseType == ValueBaseType.Enum && index == 0)
                throw new System.NotImplementedException();
            else if(baseType != ValueBaseType.Enum && index != 0)
                throw new System.NotImplementedException();
            Index = index;
        }
        public bool Equals(ValueType other)
        {
            return BaseType == other.BaseType && Index == other.Index;
        }
        public override bool Equals(object? obj)
        {
            return obj is ValueType other && Equals(other);
        }
        public override int GetHashCode()
        {
            return HashCode.Combine((int)BaseType, Index);
        }
        public static bool operator ==(ValueType left, ValueType right)
        {
            return left.Equals(right);
        }
        public static bool operator !=(ValueType left, ValueType right)
        {
            return !left.Equals(right);
        }
    }
    public enum ValueBaseType : byte
    {
        None,
        String,
        Ref,
        Number,
        Bool,
        Enum,
        EntityType
    }

    public ValueType Type;
    public static implicit operator PropertyValue(string s) => new PropertyValue
    {
        Value = s,
        IntValue = Int32.MinValue,
        Type = TypeString,
    };
    public static implicit operator PropertyValue(EntityId i) => new PropertyValue
    {
        Value = null,
        IntValue = i.Id,
        Type = TypeRef,
    };
    public static implicit operator PropertyValue(long i) => new PropertyValue
    {
        Value = null,
        IntValue = i,
        Type = TypeNumber,
    };
    public static implicit operator PropertyValue(bool b) => new PropertyValue
    {
        Value = null,
        IntValue = b ? 1 : 0,
        Type = TypeBool,
    };
    public bool BoolValue => IntValue != 0;
    public EntityId Id => new EntityId(IntValue);

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