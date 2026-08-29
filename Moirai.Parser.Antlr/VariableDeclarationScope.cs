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

    public int GetVariableIndexByName(string name, out AstVisitor.VariableDeclaration decl)
    {
        var findLastIndex = Variables.FindLastIndex(v => v.Name == name);
        if (findLastIndex == -1)
        {
            if (Parent != null)
                return Parent.GetVariableIndexByName(name, out decl);
            decl = default;
            return -1;
        }

        decl = Variables[findLastIndex];
        return findLastIndex + ParentCount;
    }
}
