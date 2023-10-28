public struct Action
{
    public string Name;
    public List<IEffect> Effects;
    // public Action(string name, IPredicate? @if, IEffect then)
    // {
    //     Name = name;
    //     Effects = new()
    //     {
    //         new PredicateParameter(@if),
    //         then,
    //     };
    // }
    public Action(string name, params IEffect[] effects)
    {
        Name = name;
        Effects = effects.ToList();
    }
}