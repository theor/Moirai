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
    }
    public readonly List<Change> Changes;
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
    public readonly PropertyType Property;
    public readonly PropertyValue PrevValue;
    public readonly PropertyValue NewValue;
    private Change(ChangeType type, EntityId entityId, PropertyType property, PropertyValue prevValue, PropertyValue newValue)
    {
        Type = type;
        EntityId = entityId;
        PrevValue = prevValue;
        NewValue = newValue;
        Property = property;
    }
    public static Change Set(EntityId entityId, PropertyType propertyType, PropertyValue prevValue, PropertyValue newValue)
    {
        return new Change(ChangeType.Set, entityId, propertyType, prevValue, newValue);
    }
    public static Change Create(EntityId entityId, EntityType type)
    {
        return new Change(ChangeType.Create, entityId, PropertyType.Id, default, (int)type);
    }

    public override string ToString()
    {
        switch (Type)
        {

            case ChangeType.Create:
                return $"Create {EntityId}: {(EntityType)NewValue.IntValue}";
            case ChangeType.Set:
                return $"Set {EntityId}.{Property}: {StoryPrinter.Print(PrevValue, Property)} -> {StoryPrinter.Print(NewValue, Property)}";
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}