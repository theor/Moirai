public struct Entity
{
    public EntityId Id;
    public List<Property>? Properties;
    public Entity(params Property[] properties) : this()
    {
        Properties ??= new();
        Properties.AddRange(properties);
    }
    public bool TryGetProperty(PropertyId property, out PropertyValue value)
    {
        if (property == Database.PropId)
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
    public PropertyValue GetProperty(PropertyId property)
    {
        if(!property.IsValid)
            throw new System.NotImplementedException("Null property");
        if (property == Database.PropId)
            return Id;
        if (Properties != null)
            return Properties.FirstOrDefault(p => p.Type == property).Value;

        return default;
    }
}