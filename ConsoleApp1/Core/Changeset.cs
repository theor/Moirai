namespace Pcg.Core;

public class History
{
    public readonly List<Changeset> Changesets = new();
}

public struct Changeset
{
    public readonly string ActionName;
    public Changeset(string actionName)
    {
        Changes = new List<Change>();
        ActionName = actionName;
        Description = null;
    }
    public readonly List<Change> Changes;
    public string? Description;
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
    public static Change Create(EntityId entityId, EntityType type, string? name = null)
    {
        return new Change(ChangeType.Create, entityId, Database.PropId, name!, (int)type);
    }

    public string ToString(Database db)
    {
        switch (Type)
        {

            case ChangeType.Create:
                return $"CREATE {PrevValue.Value} {(EntityType)NewValue.IntValue} {EntityId}";
            case ChangeType.Set:
                return $"SET {EntityId}.{db.GetPropertyName(Property)}: {StoryPrinter.Print(PrevValue, Property)} -> {StoryPrinter.Print(NewValue, Property)}";
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}