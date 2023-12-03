public readonly struct EntityType
{
    public readonly string Name;
    public readonly EntityTypeId Id;
    public EntityType(string name, uint id)
    {
        Name = name;
        Id = new EntityTypeId(id);
    }
    public PropertyValue.ValueType RefType =>
        new PropertyValue.ValueType(PropertyValue.ValueBaseType.Ref, (ushort)Id.Id);

    public readonly List<Display> Attributes { get; } = new();
}
