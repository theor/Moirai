using Moirai.Parser.Ast;

namespace Moirai.Parser;

internal static class ParsingExtensions
{
    public static string TrimQuotes(this string s) => s.Trim('"', '\'');

    /// The raw (still-escaped, i.e. `\'` not yet unescaped) text of a string literal's non-interpolated
    /// content — mirrors the old `MoiraiParser.StringContext.GetText().TrimQuotes()`. Actual unescaping
    /// happens the same place it always did: AstVisitor.ParseInterpolatedString's `.Replace("\\'", "'")`.
    public static string GetString(this StringNode node) =>
        string.Concat(node.Parts.OfType<StringTextPart>().Select(p => p.Text));
}
