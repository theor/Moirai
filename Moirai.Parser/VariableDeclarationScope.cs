namespace Moirai.Parser;

public class VariableDeclarationScope(VariableDeclarationScope? parent, FileRange range)
{
    public readonly int ParentCount = parent == null ? 0 : parent.ParentCount + parent.Variables.Count;
    public readonly FileRange Range = range;
    public readonly VariableDeclarationScope? Parent = parent;
    public readonly List<VariableDeclarationScope> Children = new();
    public readonly List<AstVisitor.VariableDeclaration> Variables = new();
    public int Count => ParentCount + Variables.Count;
    public AstVisitor.VariableDeclaration this[int index] => index < ParentCount ? Parent![index] : Variables[index - ParentCount];

    public bool GetDeclarationAndRange(int index, out AstVisitor.VariableDeclaration decl, out FileRange range)
    {
        if (index == -1)
        {
            decl = default;
            range = null;
            return false;
        }

        if (index < ParentCount)
            return Parent!.GetDeclarationAndRange(index, out decl, out range);
        decl = Variables[index - ParentCount];
        range = Range;
        return true;
    }

    /// Set on a scope whose code runs apart from the scopes around it -- a schedule block runs in a later
    /// year, when the rule's locals are gone. Its variables still take slots after its parents' (the slot
    /// numbering is the runtime's), but a name found above it is a mistake, which
    /// <see cref="GetVariableIndexByName(string, out AstVisitor.VariableDeclaration, out VariableDeclarationScope?)"/>
    /// reports by naming the isolating scope.
    public string? IsolatedBy { get; set; }

    public int GetVariableIndexByName(string name, out AstVisitor.VariableDeclaration decl) =>
        GetVariableIndexByName(name, out decl, out _);

    /// <paramref name="crossed"/> is the isolated scope the lookup had to leave to find the name, if any.
    public int GetVariableIndexByName(string name, out AstVisitor.VariableDeclaration decl, out VariableDeclarationScope? crossed)
    {
        var findLastIndex = Variables.FindLastIndex(v => v.Name == name);
        if (findLastIndex == -1)
        {
            crossed = null;
            if (Parent == null)
            {
                decl = default;
                return -1;
            }

            var index = Parent.GetVariableIndexByName(name, out decl, out var crossedAbove);
            crossed = IsolatedBy != null && index != -1 ? this : crossedAbove;
            return index;
        }

        crossed = null;
        decl = Variables[findLastIndex];
        return findLastIndex + ParentCount;
    }
}
