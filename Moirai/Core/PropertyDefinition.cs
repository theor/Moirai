public readonly struct PropertyDefinition
{
    public readonly string Name;
    private readonly uint Id;
    private readonly EntityTypeId TypeId;
    public readonly PropertyValue.ValueType Type;
    /// <summary>
    /// True for a multi-valued property (<c>prop xs: [T]</c>). <see cref="Type"/> holds the element
    /// type. Collection props have no scalar column in the wide <c>entity</c> table; their values live
    /// in the <c>collection</c> child table and are read via contains/count/pick/each.
    /// </summary>
    public readonly bool IsCollection;
    public PropertyDefinition(string name,EntityTypeId typeId, uint id, PropertyValue.ValueType type, bool isCollection = false)
    {
        Name = name;
        Id = id;
        Type = type;
        TypeId = typeId;
        IsCollection = isCollection;
    }
    public PropertyId PropertyId => new PropertyId(Id, TypeId);
}
