public readonly struct PropertyDefinition
{
    public readonly string Name;
    private readonly uint Id;
    public readonly PropertyValue.ValueType Type;
    public PropertyDefinition(string name, uint id, PropertyValue.ValueType type)
    {
        Name = name;
        Id = id;
        Type = type;
    }
    public PropertyId PropertyId => new PropertyId(Id);
}
