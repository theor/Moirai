public readonly struct EntityType
{
    public readonly string Name;
    public readonly EntityTypeId Id;
    public readonly List<PropertyDefinition> Properties = Database.DefaultProperties();

    public EntityType(string name, uint id)
    {
        Name = name;
        Id = new EntityTypeId(id);
    }
    public PropertyValue.ValueType RefType =>
        new PropertyValue.ValueType(PropertyValue.ValueBaseType.Ref, (ushort)Id.Id);

    public readonly List<Display> Attributes { get; } = new();

    public EntityType DeclareProperty(PropertyDefinition propertyDefinition)
    {
        Properties.Add(propertyDefinition);
        return this;
    }

    public EntityType DeclareProperty(string propertyDefinition, uint propYearId, PropertyValue.ValueType valueType)
    {
        Properties.Add(new PropertyDefinition(propertyDefinition, Id, propYearId, valueType));
    }
}
