using Moirai.Parser;

namespace TestProject1;

/// Pins FileRange's line/column convention (ANTLR is 1-based line / 0-based column; FileRange
/// subtracts 1 from the line so FilePosition ends up 0-based on both axes) against known source
/// coordinates. This is the migration seam for the ANTLR->Superpower port (see
/// C:\Users\theor\.claude\plans\stateful-dancing-stroustrup.md, Phase 0 step 5) — the new
/// Superpower-based FileRange constructor must reproduce these exact numbers, or every LSP/engine
/// position silently shifts by one.
public class FileRangePositionTests : TestsBase
{
    const string Source =
        "entity A {\n" +
        "    prop x: number\n" +
        "}\n";

    [Test]
    public void TypeDefinitionRange_PinnedToKnownCoordinates()
    {
        StoryParser.SetupParser(Source, out var parser, new PinningVisitor());
        var r = parser.r();
        var typeDef = r.def(0).type_definition();
        Assert.IsNotNull(typeDef);

        var range = new FileRange(typeDef);

        // "entity A {" starts at line 1, column 0 (1-based/0-based ANTLR convention) ->
        // FileRange's 0-based line is 1 - 1 = 0.
        Assert.AreEqual(0, range.Start.Line);
        Assert.AreEqual(0, range.Start.Column);

        // The grammar rule is `type_definition: ... SCOPE_CLOSE LINE_BREAK+;` — its Stop token is
        // the trailing LINE_BREAK *after* '}', not '}' itself, so the range extends one token past
        // where it visually looks like it ends. Line 3 col 0 is '}'; the '\n' right after it is at
        // line 3 col 1 -> 0-based (2, 1). Pin this exact (surprising but current) behavior.
        Assert.AreEqual(2, range.End.Line);
        Assert.AreEqual(1, range.End.Column);
    }

    [Test]
    public void PropDefinitionRange_PinnedToKnownCoordinates()
    {
        StoryParser.SetupParser(Source, out var parser, new PinningVisitor());
        var r = parser.r();
        var typeDef = r.def(0).type_definition();
        var propDef = typeDef.prop_definition(0);

        var range = new FileRange(propDef);

        // "    prop x: number" is on line 2 (1-based) -> 0-based line 1, starting at column 4.
        Assert.AreEqual(1, range.Start.Line);
        Assert.AreEqual(4, range.Start.Column);

        // Same trailing-LINE_BREAK-in-the-rule effect as above: `prop_definition: ... type
        // LINE_BREAK+ ;` — Stop is the '\n' after "number", not the "number" token. "    prop x:
        // number" is 18 chars (0-based columns 0..17), so the '\n' sits at column 18.
        Assert.AreEqual(1, range.End.Line);
        Assert.AreEqual(18, range.End.Column);
    }

    [Test]
    public void TerminalNodeRange_PinnedToKnownCoordinates()
    {
        StoryParser.SetupParser(Source, out var parser, new PinningVisitor());
        var r = parser.r();
        var typeDef = r.def(0).type_definition();
        var typeIdTerminal = typeDef.TYPE_ID();

        // A single ITerminalNode ("A") is a zero-width-name token at line 1, column 7.
        var range = new FileRange(typeIdTerminal);
        Assert.AreEqual(0, range.Start.Line);
        Assert.AreEqual(7, range.Start.Column);
        Assert.AreEqual(0, range.End.Line);
        Assert.AreEqual(8, range.End.Column);
    }

    class PinningVisitor : StoryParser.IVisitor
    {
        public List<StoryParser.Error> Errors { get; } = new();
        public MoiraiParser Parser { get; set; } = null!;
        public (int offsetLine, int offsetColumn) Offset { get; set; }
    }
}
