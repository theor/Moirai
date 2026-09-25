using System.Globalization;

// IEquatable, not just the default struct equality: a dictionary or set keyed on an EntityId (or a tuple
// holding one, as the since_last marks are) otherwise boxes both sides of every comparison.
public struct EntityId : IEquatable<EntityId>
{
    public static readonly EntityId Null = default;
    public readonly bool IsNull => Id == 0;
    public readonly uint Id;
    public EntityId(uint id)
    {
        Id = id;
    }

    public readonly bool Equals(EntityId other) => Id == other.Id;
    public override readonly bool Equals(object? obj) => obj is EntityId other && Equals(other);
    public override readonly int GetHashCode() => (int)Id;

    public override string ToString()
    {
        return $"#{Id}";
    }
}


public struct PropertyValue : IEquatable<PropertyValue>
{
    public readonly ValueType Type;

    // A string value's text: a string, or a stretch of a world's own char slab (Database.StringOf), in which
    // case it is IntValue chars from _textOffset. The slab is how a name made during a pass -- a birth's
    // "Aldric Ashford" -- is stored without allocating a string for it. The offset sits in what was padding:
    // the struct is 24 bytes either way, which every Property, PropRecord and slab is sized by.
    private readonly object? _text;
    private readonly int _textOffset;
    public readonly int IntValue;
    public readonly float FloatValue;

    /// <summary>
    /// The text of a string value, null for any other. A slab-held value builds the string each time it is
    /// read: that is for readers -- the viewer, a test -- and the simulation reads <see cref="TextSpan"/>.
    /// </summary>
    [System.Text.Json.Serialization.JsonInclude]
    public string? Value => _text switch
    {
        null => null,
        string s => s,
        _ => new string((char[])_text, _textOffset, IntValue),
    };

    /// <summary>Whether this is a string value: <c>Value != null</c>, without building the string.</summary>
    public bool HasText => _text != null;

    /// <summary>The text of a string value, empty for any other, without building a string.</summary>
    public ReadOnlySpan<char> TextSpan => _text switch
    {
        null => default,
        string s => s,
        _ => new ReadOnlySpan<char>((char[])_text, _textOffset, IntValue),
    };

    /// <summary>Whether this and <paramref name="other"/> hold their text in the same place (or neither has
    /// any): the same text for certain, found without comparing it.</summary>
    internal bool SameTextStorage(in PropertyValue other) =>
        ReferenceEquals(_text, other._text) && _textOffset == other._textOffset;
    
    public static readonly ValueType TypeString = new ValueType(ValueBaseType.String, 0);
    public static readonly ValueType TypeRef = new ValueType(ValueBaseType.Ref, 0);
    public static readonly ValueType TypeNumber = new ValueType(ValueBaseType.Number, 0);
    public static readonly ValueType TypePercent = new ValueType(ValueBaseType.Percentage, 0);
    public static readonly ValueType TypeFloat = new ValueType(ValueBaseType.Float, 0);
    public static readonly ValueType TypeBool = new ValueType(ValueBaseType.Bool, 0);
    public static readonly ValueType TypeEntityType = new ValueType(ValueBaseType.EntityType, 0);
    public static ValueType TypeTypedRef(EntityTypeId ed) => new ValueType(ValueBaseType.Ref, (ushort)ed.Id);
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

        public bool IsRefType => BaseType == ValueBaseType.Ref || BaseType == ValueBaseType.EntityType;
        public static readonly ValueType Null = default;

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

        public EntityTypeId ToEntityType()
        {
            if (!IsRefType)
                throw new InvalidCastException();
            return new EntityTypeId(Index);
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
        EntityType,
        Percentage
    }

    public PropertyValue(ValueType type, int intValue)
    {
        Type = type;
        _text = null;
        _textOffset = 0;
        if (type.BaseType == ValueBaseType.Percentage)
            intValue = Math.Clamp(intValue, 0, 100);
        IntValue = intValue;
        FloatValue = intValue;
    }
    public PropertyValue(ValueType type, float floatValue)
    {
        Type = type;
        _text = null;
        _textOffset = 0;
        if (type.BaseType == ValueBaseType.Percentage)
            floatValue = Math.Clamp(floatValue, 0f, 100f);
        IntValue = (int)floatValue;
        FloatValue = floatValue;
    }
    public PropertyValue(string s)
    {
        Type = TypeString;
        _text = s;
        _textOffset = 0;
        IntValue = s.Length;
        FloatValue = s.Length;
    }

    /// <summary>A string value whose text is <paramref name="length"/> chars of a slab from <paramref name="offset"/>.</summary>
    internal PropertyValue(char[] slab, int offset, int length)
    {
        Type = TypeString;
        _text = slab;
        _textOffset = offset;
        IntValue = length;
        FloatValue = length;
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

    // As before the text could live in a slab: equal when both have no text and the same IntValue, or both
    // have the same text -- compared by content, wherever each keeps it. A string never equals a number.
    public bool Equals(PropertyValue other)
    {
        if (IntValue != other.IntValue || HasText != other.HasText)
            return false;
        return !HasText || SameTextStorage(other) || TextSpan.SequenceEqual(other.TextSpan);
    }
    public override bool Equals(object? obj)
    {
        return obj is PropertyValue other && Equals(other);
    }
    public override int GetHashCode()
    {
        return HashCode.Combine(HasText ? string.GetHashCode(TextSpan) : 0, IntValue);
    }
    public static bool operator ==(PropertyValue left, PropertyValue right)
    {
        return left.Equals(right);
    }
    public static bool operator !=(PropertyValue left, PropertyValue right)
    {
        return !left.Equals(right);
    }

    public static PropertyValue Percent(int i)
    {
        return new PropertyValue(TypePercent, i);
    }
}
