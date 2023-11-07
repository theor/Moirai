public readonly struct EntityType
{
    public readonly string Name;
    public readonly EntityTypeId Id;
    public EntityType(string name, uint id)
    {
        Name = name;
        Id = new EntityTypeId(id);
    }
}