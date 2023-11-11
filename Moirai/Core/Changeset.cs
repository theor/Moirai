namespace Moirai.Core;

public class History
{
    public readonly HistoryMode Mode;
    public readonly List<Changeset> Changesets = new();

    public History(HistoryMode mode = HistoryMode.Default)
    {
        Mode = mode;
    }

    [Flags]
    public enum HistoryMode
    {
        Default = 0,

        Story = 1,
    }
}

public struct Changeset(int id, string actionName, long year, CategoryId[] cats)
{
    public readonly int Id = id;
    public readonly string ActionName = actionName;
    public readonly long Year = year;
    public readonly List<Change> Changes = new();
    public ulong Categories = cats.Aggregate(0ul, (x, y) => x | (1ul<< (int)(y.Id - 1)));
    // public string? Description { get; private set; } = null;
    // public bool HasDescription => !String.IsNullOrEmpty(Description);

    // public void AppendDescription(string? desc)
    // {
    //     if (!String.IsNullOrEmpty(desc))
    //     {
    //         if (!String.IsNullOrEmpty(this.Description))
    //             this.Description += "\n";
    //         // else
    //         //     CurrentChangeset.Description = $"{Year}\n";
    //         this.Description += desc;
    //     }
    // }
    public void GetAffectedEntities(HashSet<EntityId> changedEntities)
    {
        foreach (var change in this.Changes)
        {
            changedEntities.Add(change.EntityId);
            if (change.PrevValue.Type == PropertyValue.TypeRef && !change.PrevValue.Id.IsNull)
                changedEntities.Add(change.PrevValue.Id);
            if (change.NewValue.Type == PropertyValue.TypeRef && !change.NewValue.Id.IsNull)
                changedEntities.Add(change.NewValue.Id);
        }
    }
}

public struct Change
{
    public enum ChangeType
    {
        Create,
        Set,
    }

    public readonly ChangeType Type;
    public readonly EntityId EntityId;
    public readonly PropertyId Property;
    public readonly PropertyValue PrevValue;
    public readonly PropertyValue NewValue;
    private Change(ChangeType type, EntityId entityId, PropertyId property, PropertyValue prevValue, PropertyValue newValue)
    {
        Type = type;
        EntityId = entityId;
        PrevValue = prevValue;
        NewValue = newValue;
        Property = property;
    }
    public static Change Set(EntityId entityId, PropertyId propertyType, PropertyValue prevValue, PropertyValue newValue)
    {
        return new Change(ChangeType.Set, entityId, propertyType, prevValue, newValue);
    }
    public static Change Create(EntityId entityId, EntityTypeId type, string? name = null)
    {
        return new Change(ChangeType.Create, entityId, Database.PropId, name!, type.Id);
    }

    public string ToString(Database db)
    {
        switch (Type)
        {

            case ChangeType.Create:
                return $"CREATE {PrevValue.Value} {db.GetEntityTypeName(NewValue.TypeId)} {EntityId}";
            case ChangeType.Set:
                return $"SET {EntityId}.{db.GetPropertyName(Property)}: {db.Printer.Print(PrevValue)} -> {db.Printer.Print(NewValue)}";
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
