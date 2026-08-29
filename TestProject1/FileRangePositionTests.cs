using Moirai.Parser;
using Moirai.Parser.Ast;

namespace TestProject1;

/// Pins FileRange's line/column convention against known source coordinates -- the migration seam
/// for the ANTLR->Superpower port (see C:\Users\theor\.claude\plans\stateful-dancing-stroustrup.md).
/// Originally written in Phase 0 against the ANTLR-based FileRange constructor; retargeted here in
/// Phase 3 at the new TextSpan-based one (Superpower positions are 1-based on both line and column,
/// unlike ANTLR's 1-based-line/0-based-column, so both axes get a "-1" now). Unlike the old
/// ANTLR-based FileRange (whose End was the *start* of the rule's last consumed token -- a quirk the
/// original version of this file documented and pinned), End here is the true end of the span.
public class FileRangePositionTests : TestsBase
{
    const string Source =
        "entity A {\n" +
        "    prop x: number\n" +
        "}\n";

    static RNode Parse()
    {
        var tokenized = MoiraiTokenizer.Tokenize(Source);
        Assert.That(tokenized.Errors, Is.Empty);
        var result = MoiraiGrammar.TryParseR(tokenized.ParseTokens);
        Assert.That(result.HasValue, Is.True, () => result.ToString());
        return result.Value;
    }

    [Test]
    public void TypeDefinitionRange_PinnedToKnownCoordinates()
    {
        var typeDef = Parse().Defs[0].TypeDefinition;
        Assert.IsNotNull(typeDef);

        var range = new FileRange(typeDef!.Span);

        // "entity A {" starts at line 1, column 1 (Superpower's 1-based/1-based convention) ->
        // FileRange's 0-based line/column is 1 - 1 = 0.
        Assert.AreEqual(0, range.Start.Line);
        Assert.AreEqual(0, range.Start.Column);

        // TypeDefinitionNode's span ends at the closing '}' itself (line 3, 0-based column 0) --
        // End is one character past that, i.e. (2, 1).
        Assert.AreEqual(2, range.End.Line);
        Assert.AreEqual(1, range.End.Column);
    }

    [Test]
    public void PropDefinitionRange_PinnedToKnownCoordinates()
    {
        var typeDef = Parse().Defs[0].TypeDefinition!;
        var propDef = typeDef.PropDefinitions[0];

        var range = new FileRange(propDef.Span);

        // "    prop x: number" is on line 2 (1-based) -> 0-based line 1, starting at column 4.
        Assert.AreEqual(1, range.Start.Line);
        Assert.AreEqual(4, range.Start.Column);

        // PropDefinitionNode's span ends at the type ("number"), not the trailing line break --
        // "    prop x: number" is 18 chars (0-based columns 0..17), so End.Column is 18.
        Assert.AreEqual(1, range.End.Line);
        Assert.AreEqual(18, range.End.Column);
    }

    [Test]
    public void IdentRange_PinnedToKnownCoordinates()
    {
        var typeDef = Parse().Defs[0].TypeDefinition!;

        // The type name ("A") is a single-character token at line 1, column 7.
        var range = new FileRange(typeDef.TypeName!.Value.Span);
        Assert.AreEqual(0, range.Start.Line);
        Assert.AreEqual(7, range.Start.Column);
        Assert.AreEqual(0, range.End.Line);
        Assert.AreEqual(8, range.End.Column);
    }
}
