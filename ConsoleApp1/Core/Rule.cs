struct Rule
{
    public string Name;
    public IPredicate If;
    public IPredicate Then;
    public Rule(string name, IPredicate @if, IPredicate then)
    {
        Name = name;
        If = @if;
        Then = then;
    }
}