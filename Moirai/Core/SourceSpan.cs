namespace Moirai.Core;

/// <summary>
/// A 0-based line/column span into the source <c>.sg</c> file, attached to runtime
/// instructions so a debugger can map execution back to source. <see cref="None"/>
/// represents an unknown/unset position. This is the engine-local counterpart of the
/// parser's <c>FileRange</c> (the engine does not reference the parser/ANTLR).
/// </summary>
public readonly record struct SourceSpan(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    public static readonly SourceSpan None = new(-1, -1, -1, -1);

    public bool IsValid => StartLine >= 0;

    /// True if the given 0-based position lies within this span (inclusive).
    public bool Contains(int line, int column)
    {
        if (line < StartLine || line > EndLine) return false;
        if (line == StartLine && column < StartColumn) return false;
        if (line == EndLine && column > EndColumn) return false;
        return true;
    }
}
