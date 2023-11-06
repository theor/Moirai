using Pcg.Core;

public struct Action(string name, bool isEvent,IFilter? filter)
{
    public readonly string Name = name;
    public readonly bool IsEvent = isEvent;

    public readonly List<IInstruction> Effects = new();
    public readonly List<AssignPick> Whens = new();

    public IFilter? Filter = filter;
}