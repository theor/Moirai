using Moirai.Parser;

namespace TestProject1;

/// Corpus-wide regression cover for the parser, at the two levels that matter: what the source
/// lowers to, and what the simulation then does.
///
/// Replaces ParserDifferentialTests, which asserted the same two things against the frozen ANTLR
/// parser. Comparing the printed Database rather than structurally diffing an object graph is
/// inherited from that suite -- StoryPrinter is already a canonical serialization of the whole
/// world, so it makes an excellent snapshot.
public class ParserGoldenTests : TestsBase
{
    static string GoldenName(string relativePath, string suffix) =>
        relativePath.Replace('/', '_') + suffix;

    [Test]
    [TestCaseSource(typeof(Golden), nameof(Golden.Corpus))]
    public void CorpusFile_ParsesToTheSameWorld(string relativePath)
    {
        var db = StoryParser.Parse(Golden.Read(relativePath), out var errors);

        Assert.That(errors, Is.Empty, () => string.Join("\n", errors));
        Golden.Verify(GoldenName(relativePath, ".printed"), db.Printer.Print());
    }

    /// The engine is deterministic per seed, so the record stream is a stable fingerprint of every
    /// semantic decision the parser made -- it catches divergences the printed form cannot show.
    [Test]
    [TestCaseSource(typeof(Golden), nameof(Golden.Corpus))]
    public void CorpusFile_SimulatesTheSameHistory(string relativePath)
    {
        var db = StoryParser.Parse(Golden.Read(relativePath), out _);
        db.Init();
        db.Ctx.PassYears(50, true);

        Golden.Verify(GoldenName(relativePath, ".records"),
            string.Join("\n", db.Records.Select(r => r.Text)) + "\n");
    }
}
