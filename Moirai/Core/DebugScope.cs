namespace Moirai.Core;

/// <summary>
/// Debug-only metadata describing the lexical variable scopes of a single
/// <c>event</c>/<c>trigger</c>/<c>function</c>. Built by the parser from its own scope
/// tree and attached to <see cref="EventTrigger"/> / <see cref="FunctionDefinition"/>.
///
/// At runtime variables are bare value-stack slots (see <see cref="ExecuteContext"/>);
/// sibling scopes reuse the same slot range, so a slot's name depends on which branch is
/// executing. The debugger resolves names by finding the <see cref="Innermost"/> scope
/// containing the current instruction's position, then enumerating
/// <see cref="VisibleVariables"/> (that scope plus its ancestors).
/// </summary>
public sealed class DebugScope
{
    private readonly List<DebugScope> _children = new();
    private readonly List<(int Slot, string Name)> _variables = new();

    public SourceSpan Range { get; }
    public DebugScope? Parent { get; private set; }
    public IReadOnlyList<DebugScope> Children => _children;
    public IReadOnlyList<(int Slot, string Name)> Variables => _variables;

    public DebugScope(SourceSpan range) => Range = range;

    public void AddVariable(int slot, string name) => _variables.Add((slot, name));

    public void AddChild(DebugScope child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    /// The deepest descendant scope whose range contains <paramref name="position"/>.
    public DebugScope Innermost(SourceSpan position)
    {
        foreach (var c in _children)
            if (c.Range.Contains(position.StartLine, position.StartColumn))
                return c.Innermost(position);
        return this;
    }

    /// Variables visible from this scope outward (this scope first, then ancestors).
    public IEnumerable<(int Slot, string Name)> VisibleVariables()
    {
        for (var s = this; s != null; s = s.Parent)
            foreach (var v in s._variables)
                yield return v;
    }
}
