using Moirai.Core;

public struct Action(int id, string name, bool isEvent,IFilter? filter, CategoryId[] tags = null)
{
    public readonly int Id = id;
    public readonly string Name = name;
    public readonly bool IsEvent = isEvent;

    public readonly List<IInstruction> Effects = new();
    public readonly List<AssignPick> Whens = new();
    public readonly CategoryId[] Categories = tags ?? Array.Empty<CategoryId>();

    public IFilter? Filter = filter;
    public List<TagId> WhenTags = new();
}
