public struct Property
{
    public PropertyType Type;
    public PropertyValue Value;
    public Property(PropertyType type, PropertyValue value)
    {
        Type = type;
        Value = value;
    }
}

public enum PropertyType
{
    Id,
    Type,
    Alive,
    Owner,
    Partner,
    Name
}