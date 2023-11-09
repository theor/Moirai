using System.Diagnostics;

[DebuggerTypeProxy(typeof(PropertyProxy))]
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

public class PropertyProxy
{
    public string? Name;
    public PropertyId Id;
    public string Value;
    public PropertyProxy(Property p)
    {
        Name = Database.Instance?.GetPropertyName(p.Id);
        Id = p.Id;
        Value = Value.ToString();
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