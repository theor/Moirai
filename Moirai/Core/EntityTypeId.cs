public readonly struct EntityTypeId : IEquatable<EntityTypeId>
{
    public readonly uint Id;
    public static readonly EntityTypeId Null = new EntityTypeId(0);
    public EntityTypeId(uint id)
    {
        Id = id;
    }
    public bool IsValid => Id != 0;

    public bool Equals(EntityTypeId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is EntityTypeId other && Equals(other);
    public override int GetHashCode() => (int)Id;
    public static bool operator ==(EntityTypeId left, EntityTypeId right) => left.Equals(right);
    public static bool operator !=(EntityTypeId left, EntityTypeId right) => !left.Equals(right);
    public override string ToString()
    {

        if (Database.Instance != null)
        {
            return $"t{Id}:{Database.Instance.Types[(int)Id].Name}";
        }
        return $"t{Id}";
    }
}
