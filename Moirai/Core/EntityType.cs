public readonly struct EntityType
{
    public readonly string Name;
    public readonly EntityTypeId Id;
    public readonly List<PropertyDefinition> Properties = Database.DefaultProperties();
    public EntityType(string name, uint id, IEnumerable<PropertyDefinition> props)
    {
        Name = name;
        Id = new EntityTypeId(id);
        var eid = Id;
        Properties.AddRange(props.Select(p => new PropertyDefinition(p.Name, p.Id, p.Type, eid)));
    }

    public PropertyValue.ValueType RefType =>
        new PropertyValue.ValueType(PropertyValue.ValueBaseType.Ref, (ushort)Id.Id);

    public string GetPropertyName(PropertyId prop)
    {
        return Properties?[(int)prop.Id].Name;
    }

    public PropertyId GetPropertyId(string name)
    {
        return Properties?.FirstOrDefault(x => x.Name == name).PropertyId ?? default;
    }
}
