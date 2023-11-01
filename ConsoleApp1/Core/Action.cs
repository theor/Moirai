public readonly struct Action
{
    public readonly string Name;
    public readonly bool IsEvent;

    public readonly List<IEffect> Effects;
    public readonly List<AssignPick> Whens;
    public readonly List<FormatAction> Formats;
    // public Action(string name, IPredicate? @if, IEffect then)
    // {
    //     Name = name;
    //     Effects = new()
    //     {
    //         new PredicateParameter(@if),
    //         then,
    //     };
    // }
    public Action(string name, bool isEvent)
    {
        IsEvent = isEvent;
        Name = name;
        Formats = new List<FormatAction>();
        Effects = new();
        Whens = new();
    }
}