struct Rule
{
    public string Name;
    public IValue If;
    public IValue Then;
    public Rule(string name, IValue @if, IValue then)
    {
        Name = name;
        If = @if;
        Then = then;
    }
}