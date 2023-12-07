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
    public struct Changed(Entity prev, Entity @new)
    {
        public readonly Entity Prev = prev;
        public readonly Entity New = @new;
    }
    public readonly int Id = id;
    public readonly string ActionName = actionName;
    public readonly long Year = year;
    // public readonly List<Change> Changes = new();
    private List<Changed>? _changes;
    public IReadOnlyCollection<Changed> Changes => _changes as IReadOnlyCollection<Changed> ?? ArraySegment<Changed>.Empty;
    public readonly ulong Categories = cats.Aggregate(0ul, (x, y) => x | (1ul<< (int)(y.Id - 1)));
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
    // public void GetAffectedEntities(HashSet<EntityId> changedEntities)
    // {
    //     foreach (var change in this.Changes)
    //     {
    //         changedEntities.Add(change.EntityId);
    //         if (change.PrevValue.Type == PropertyValue.TypeRef && !change.PrevValue.Id.IsNull)
    //             changedEntities.Add(change.PrevValue.Id);
    //         if (change.NewValue.Type == PropertyValue.TypeRef && !change.NewValue.Id.IsNull)
    //             changedEntities.Add(change.NewValue.Id);
    //     }
    // }

    // public void GetTaggedEntities(List<(EntityId, TagId)> taggedEntities)
    // {
    //     foreach (var change in Changes)
    //     {
    //         if(change.Type == Change.ChangeType.AddTag)
    //             taggedEntities.Add((change.EntityId, change.Tag));
    //     }
    // }
    public void RecordSet(Entity modifiedEntity, PropertyId property, PropertyValue prev)
    {
        var i = -1;
        if (_changes != null)
            i = _changes.FindIndex(c => c.New.Id.Id == modifiedEntity.Id.Id);
        _changes ??= new();
        if (i == -1)
        {
            // TODO remove db singleton
            var prevEntity = new Entity(Database.Instance.GetEntityType(modifiedEntity.Type)){Id = modifiedEntity.Id};
            prevEntity.SetProperty(property, prev);
            _changes.Add(new Changed(prevEntity, modifiedEntity));
        }
        else
        {
            if(!_changes[i].Prev.Id.IsNull) // no point in recording a newly created entity's previous property value
                _changes[i].Prev.SetProperty(property, prev);
        }
    }

    public void RecordCreate(Entity @new)
    {
        _changes ??= new List<Changed>();
        _changes.Add(new Changed(default, @new));
    }
}

// public struct Change
// {
//     public enum ChangeType
//     {
//         Create,
//         Set,
//         AddTag,
//     }
//
//     public readonly ChangeType Type;
//     public readonly EntityId EntityId;
//     public readonly PropertyId Property;
//     public readonly PropertyValue PrevValue;
//     public readonly PropertyValue NewValue;
//     public readonly TagId Tag;
//     private Change(ChangeType type, EntityId entityId, PropertyId property, PropertyValue prevValue, PropertyValue newValue, TagId tag)
//     {
//         Type = type;
//         EntityId = entityId;
//         PrevValue = prevValue;
//         NewValue = newValue;
//         Property = property;
//         Tag = tag;
//     }
//     public static Change Set(EntityId entityId, PropertyId propertyType, PropertyValue prevValue, PropertyValue newValue)
//     {
//         return new Change(ChangeType.Set, entityId, propertyType, prevValue, newValue, default);
//     }
//     public static Change Create(EntityId entityId, EntityTypeId type, string? name = null)
//     {
//         return new Change(ChangeType.Create, entityId, Database.PropId, name!, type.Id, default);
//     }
//     public static Change AddTag(EntityId entityId, TagId tag)
//     {
//         return new Change(ChangeType.AddTag, entityId, default, default, default, tag);
//     }
//
//     public string ToString(Database db)
//     {
//         switch (Type)
//         {
//
//             case ChangeType.Create:
//                 return $"CREATE {PrevValue.Value} {db.GetEntityTypeName(NewValue.TypeId)} {EntityId}";
//             case ChangeType.Set:
//                 return $"SET {EntityId}.{db.GetPropertyName(Property)}: {db.Printer.Print(PrevValue)} -> {db.Printer.Print(NewValue)}";
//             case ChangeType.AddTag:
//                 return $"ADD TAG {EntityId} {db.GetTagName(Tag)}";
//             default:
//                 throw new ArgumentOutOfRangeException();
//         }
//     }
// }
