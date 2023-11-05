using Pcg.Core;

public struct Action
{
    public readonly string Name;
    public readonly bool IsEvent;

    public readonly List<IInstruction> Effects;
    public readonly List<AssignPick> Whens;

    public RandomEvent Random;

    public readonly bool IsStartAction;
    // public Action(string name, IPredicate? @if, IEffect then)
    // {
    //     Name = name;
    //     Effects = new()
    //     {
    //         new PredicateParameter(@if),
    //         then,
    //     };
    // }
    public Action(string name, bool isEvent, RandomEvent random = default, bool isStartAction = false)
    {
        IsEvent = isEvent;
        Name = name;
        Effects = new();
        Whens = new();
        Random = random;
        IsStartAction = isStartAction;
    }
}