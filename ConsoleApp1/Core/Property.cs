public struct Property
{
    public PropertyId Type;
    public PropertyValue Value;
    public Property(PropertyId type, PropertyValue value)
    {
        Type = type;
        Value = value;
    }
}
//
// public enum PropertyType
// {
//     Id,
//     Type,
//     // Alive,
//     // Owner,
//     // Partner,
//     Name,
//     // Faction,
// }