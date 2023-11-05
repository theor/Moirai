namespace Pcg.Core;

public class History
{
    public readonly List<Changeset> Changesets = new();
}

public struct Changeset
{
    public readonly string ActionName;
    public readonly long Year;
    public Changeset(string actionName, long year)
    {
        Changes = new List<Change>();
        ActionName = actionName;
        Year = year;
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