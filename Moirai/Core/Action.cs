using Moirai.Core;

public struct Action(int id, string name, bool isEvent,IFilter? filter, TagId[]? tags = null)
{
    public readonly int Id = id;
    public readonly string Name = name;
    public readonly bool IsEvent = isEvent;

    public readonly List<IInstruction> Effects = new();
    public readonly List<AssignPick> Whens = new();
    public readonly TagId[] Tags = tags ?? Array.Empty<TagId>();

    public IFilter? Filter = filter;
}
