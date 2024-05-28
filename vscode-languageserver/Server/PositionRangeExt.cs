using Moirai.Parser;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

public static class PositionRangeExt
{
    public static Position ToLspPosition(this StoryParser.AstVisitor.FilePosition p) => new(p.Line, p.Column);
    public static StoryParser.AstVisitor.FilePosition ToParserPosition(this Position p) => new(p.Line, p.Character);
    public static Range ToLspRange(this StoryParser.AstVisitor.FileRange r) => new(ToLspPosition(r?.Start ?? default), ToLspPosition(r?.End ?? default));
}
