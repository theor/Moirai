using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace Moirai.Parser;

public record FileRange(FilePosition Start, FilePosition End)
{
    public static readonly FileRange Empty = new FileRange(new FilePosition(-1,-1), new FilePosition(-1,-1));
    public static implicit operator FileRange(ParserRuleContext rule) => new(rule);
    // public static implicit operator FileRange(ITerminalNode token) => new(token.Symbol);

    public FileRange(ParserRuleContext symbol) : this(
        new FilePosition(symbol.Start.Line - 1, symbol.Start.Column),
        GetEnd(symbol)
    )
    {
    }

    private static FilePosition GetEnd(ParserRuleContext symbol)
    {
        if (symbol.Stop == null || symbol.Stop == symbol.Start)
            return new(symbol.Start.Line - 1, symbol.Start.Column + symbol.GetText().Length);
        return new FilePosition(symbol.Stop.Line - 1, symbol.Stop.Column);
    }

    public FileRange(IToken symbol) : this(new FilePosition(symbol.Line - 1, symbol.Column),
        new FilePosition(symbol.Line - 1, symbol.Column + symbol.Text.Length))
    {
    }
    public FileRange(ITerminalNode symbol) : this(symbol.Symbol)
    {
    }

    public bool Contains(FilePosition pos)
    {
        return Start.Line <= pos.Line && pos.Line <= End.Line &&
               (Start.Line != pos.Line || Start.Column <= pos.Column) &&
               (End.Line != pos.Line || End.Column >= pos.Column);
    }
}
