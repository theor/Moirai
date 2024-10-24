using Moirai.Core;

public class EventTrigger(int id, string name, bool isEvent,IFilter? filter, bool skip = false, List<string>? tags = null)
{
    public List<string>? Tags { get; } = tags;

    public enum WhenType { Created, Changed, }
    public readonly int Id = id;
    public readonly string Name = name;
    public readonly bool IsTrigger = isEvent;
    public bool Skip = skip;

    public readonly List<IInstruction> Effects = new();
    public (WhenType, EntityTypeId, IValue?) When = default;

    public readonly IFilter? Filter = filter;
    // public List<TagId> WhenTags = new();
}
