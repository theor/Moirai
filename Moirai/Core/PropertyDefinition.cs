public readonly struct PropertyDefinition
{
    public readonly string Name;
    private readonly uint Id;
    private readonly EntityTypeId TypeId;
    public readonly PropertyValue.ValueType Type;
    public PropertyDefinition(string name,EntityTypeId typeId, uint id, PropertyValue.ValueType type)
    {
        Name = name;
        Id = id;
        Type = type;
        TypeId = typeId;
    }
    public PropertyId PropertyId => new PropertyId(Id, TypeId);
}
