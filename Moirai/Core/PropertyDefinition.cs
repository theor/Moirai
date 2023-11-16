public readonly struct PropertyDefinition
{
    public readonly string Name;
    public readonly uint Id;
    public readonly PropertyValue.ValueType Type;
    public readonly EntityTypeId TypeId;

    public PropertyDefinition(string name, uint id, PropertyValue.ValueType type, EntityTypeId typeId)
    {
        Name = name;
        Id = id;
        Type = type;
        TypeId = typeId;
    }

    public PropertyId PropertyId => new PropertyId(Id, TypeId);
}
