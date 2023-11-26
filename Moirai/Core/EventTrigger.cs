using Moirai.Core;

public class EventTrigger(int id, string name, bool isEvent,IFilter? filter, CategoryId[] tags = null, bool skip = false)
{
    public enum WhenType { Created, Changed, }
    public readonly int Id = id;
    public readonly string Name = name;
    public readonly bool IsTrigger = isEvent;
    public bool Skip = skip;

    public readonly List<IInstruction> Effects = new();
    public (WhenType, EntityTypeId, IValue?) When = default;
    public readonly CategoryId[] Categories = tags ?? Array.Empty<CategoryId>();

    public IFilter? Filter = filter;
    // public List<TagId> WhenTags = new();
}
