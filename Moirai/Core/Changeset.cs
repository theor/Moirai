namespace Moirai.Core;

public class History
{
    public readonly HistoryMode Mode;
    private readonly List<Changeset> _changesets = new();
    public IReadOnlyList<Changeset> Changesets => _changesets;

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

    public void AddChangeset(Changeset currentChangeset)
    {
        currentChangeset.CloseChangeset();
        _changesets.Add(currentChangeset);
    }
}

public struct Changeset(int id, string actionName, long year)
{
    // TODO: Entity prop list is by ref - the new entity is not a copy
    public struct Changed(Entity prev, Entity @new)
    {
        public readonly Entity Prev = prev;
        public readonly Entity New = @new;
    }
    public readonly int Id = id;
    public readonly string ActionName = actionName;
    public readonly long Year = year;
    private List<Changed>? _changes;
    public IReadOnlyCollection<Changed> Changes => _changes as IReadOnlyCollection<Changed> ?? ArraySegment<Changed>.Empty;

    /// <summary>
    /// for each changed entity, deep clone the new entity so its property values won't change when the actual entity is modified
    /// this ensure the changeset show values at the time of the action and not the current values
    /// </summary>
    public void CloseChangeset()
    {
        for (var index = 0; index < _changes.Count; index++)
        {
            Changed changed = _changes[index];
            _changes[index] = new Changed(changed.Prev, changed.New.Clone());
        }
    }
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
