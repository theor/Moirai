namespace Moirai.Parser;

internal static class ParsingExtensions
{
    public static string TrimQuotes(this string s) => s.Trim('"', '\'');
    public static string GetString(this MoiraiParser.StringContext context) => context.GetText().TrimQuotes();
}