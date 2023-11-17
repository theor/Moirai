using System.Globalization;
using System.Xml;

public struct EntityId
{
    public static readonly EntityId Null = default;
    public readonly bool IsNull => Id == 0;
    public readonly uint Id;
    public EntityId(uint id)
    {
        Id = id;
    }

    public override string ToString()
    {
        return $"#{Id}";
    }
}

public struct CategoryId
{
    public static readonly CategoryId Null = default;
    public readonly bool IsNull => Id == 0;
    public readonly ulong Id;
    public CategoryId(ulong id)
    {
        Id = id;
    }

    public override string ToString()
    {
        return $"{Id}";
    }
}

public struct PropertyValue : IEquatable<PropertyValue>
{
    public readonly ValueType Type;
    public readonly string? Value;
    public readonly int IntValue;
    public readonly float FloatValue;
    
    public static readonly ValueType TypeString = new ValueType(ValueBaseType.String, 0);
    public static readonly ValueType TypeRef = new ValueType(ValueBaseType.Ref, 0);
    public static readonly ValueType TypeNumber = new ValueType(ValueBaseType.Number, 0);
    public static readonly ValueType TypeFloat = new ValueType(ValueBaseType.Float, 0);
    public static readonly ValueType TypeBool = new ValueType(ValueBaseType.Bool, 0);
    public static readonly ValueType TypeEntityType = new ValueType(ValueBaseType.EntityType, 0);
    public static ValueType TypeEnumType(EnumDefinitionId ed) => new ValueType(ValueBaseType.EnumType, ed.Id);
    public static ValueType TypeEnum(EnumDefinitionId index) => new ValueType(ValueBaseType.Enum, index.Id);

    public readonly struct ValueType : IEquatable<ValueType>
    {
        public readonly ValueBaseType BaseType;
        public readonly ushort Index;
        public ValueType(ValueBaseType baseType, ushort index)
        {
            BaseType = baseType;
            if(baseType == ValueBaseType.Enum && index == 0)
                throw new System.NotImplementedException();
            else if(baseType != ValueBaseType.Enum && baseType != ValueBaseType.Ref && baseType != ValueBaseType.EnumType && index != 0)
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

        public override string ToString()
        {
            return $"{BaseType}:{Index}";
        }
    }
    public enum ValueBaseType : byte
    {
        None,
        String,
        Ref,
        Number,
        Float,
        Bool,
        Enum,
        EnumType,
        EntityType
    }

    public PropertyValue(ValueType type, int intValue)
    {
        Type = type;
        Value = null;
        IntValue = intValue;
        FloatValue = intValue;
    }
    public PropertyValue(ValueType type, float floatValue)
    {
        Type = type;
        Value = null;
        IntValue = (int)floatValue;
        FloatValue = floatValue;
    }
    public PropertyValue(string s)
    {
        Type = TypeString;
        Value = s;
        IntValue = s.Length;
        FloatValue = s.Length;
    }

    public static implicit operator PropertyValue(string s) => new PropertyValue(s);

    public static implicit operator PropertyValue(EntityId i) => new PropertyValue(TypeRef, (int)i.Id);
   
    public static implicit operator PropertyValue(EntityTypeId i) => new PropertyValue(TypeEntityType, (int)i.Id);
    public static implicit operator PropertyValue(EnumDefinitionId i) => new PropertyValue(TypeEnumType(i), (int)i.Id);
    public static implicit operator PropertyValue(int i) => new PropertyValue(TypeNumber, i);
    public static implicit operator PropertyValue(float i) => new PropertyValue(TypeFloat, i);
    public static implicit operator PropertyValue(bool b) => new PropertyValue(TypeBool, b ? 1 : 0);
    public bool BoolValue => IntValue!= 0;
    public EntityId Id => new EntityId((uint)IntValue);
    public EntityTypeId TypeId => new EntityTypeId((uint)IntValue);

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
    public string ToSql()
    {

        switch (Type.BaseType)
        {

            case PropertyValue.ValueBaseType.String:
                return $"'{Value}'";
            case PropertyValue.ValueBaseType.Ref:
            case PropertyValue.ValueBaseType.Number:
            case PropertyValue.ValueBaseType.Bool:
            case PropertyValue.ValueBaseType.Enum:
            case PropertyValue.ValueBaseType.EntityType:
                return IntValue.ToString();
            case PropertyValue.ValueBaseType.Float:
                return FloatValue.ToString(CultureInfo.InvariantCulture);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
