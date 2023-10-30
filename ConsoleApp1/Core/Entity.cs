public struct Entity
{
    public long Id;
    public List<Property>? Properties;
    public Entity(params Property[] properties) : this()
    {
        Properties ??= new();
        Properties.AddRange(properties);
    }
    public bool TryGetProperty(PropertyType property, out PropertyValue value)
    {
        if (property == PropertyType.Id)
        {
            value = Id;
            return true;
        }
        if (Properties == null)
        {
            value = default;
            return false;
        }
        foreach (var p in Properties)
        {
            if (p.Type == property)
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
    public PropertyValue GetProperty(PropertyType property)
    {
        if (property == PropertyType.Id)
            return Id;
        if (Properties != null)
            return Properties.FirstOrDefault(p => p.Type == property).Value;

        return default;
    }
}
public enum EntityType
{
    Person = 1,
    Item,
    Faction,
}