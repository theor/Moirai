public struct Property
{
    public PropertyId Id;
    public PropertyValue Value;
    public Property(PropertyId id, PropertyValue value)
    {
        Id = id;
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