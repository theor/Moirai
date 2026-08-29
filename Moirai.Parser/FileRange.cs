using Superpower.Model;

namespace Moirai.Parser;

public record FileRange(FilePosition Start, FilePosition End)
{
    public static readonly FileRange Empty = new FileRange(new FilePosition(-1,-1), new FilePosition(-1,-1));
    public static implicit operator FileRange(TextSpan span) => new(span);

    /// Built from a Superpower TextSpan (every AST node in Moirai.Parser.Ast carries one). Superpower
    /// positions are 1-based on both line and column; FileRange keeps the 0-based-on-both convention
    /// the rest of the codebase (and the LSP, via the frozen ANTLR path) already assumes, so both
    /// axes get a "-1" here -- pinned exactly by TestProject1/FileRangePositionTests's AST-based cases.
    /// Unlike the old ANTLR-based FileRange (whose End was the *start* of the rule's last consumed
    /// token, a quirk documented and pinned in that same test file), End here is the true end of the
    /// span -- the position one character past the last character it covers.
    public FileRange(TextSpan span) : this(ToFilePosition(span.Position), ToFilePosition(EndOf(span)))
    {
    }

    static FilePosition ToFilePosition(Position p) => new(p.Line - 1, p.Column - 1);

    static Position EndOf(TextSpan span)
    {
        var pos = span.Position;
        foreach (var c in span.ToStringValue())
            pos = pos.Advance(c);
        return pos;
    }

    /// Engine-local span (no ANTLR dependency) for attaching to runtime instructions.
    public Moirai.Core.SourceSpan ToSpan() =>
        new(Start.Line, Start.Column, End.Line, End.Column);

    public bool Contains(FilePosition pos)
    {
        return Start.Line <= pos.Line && pos.Line <= End.Line &&
               (Start.Line != pos.Line || Start.Column <= pos.Column) &&
               (End.Line != pos.Line || End.Column >= pos.Column);
    }
}
