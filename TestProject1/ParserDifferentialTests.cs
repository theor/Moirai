extern alias MoiraiAntlr;
using Moirai.Parser;
using AntlrStoryParser = MoiraiAntlr::Moirai.Parser.StoryParser;

namespace TestProject1;

/// Phase 3 gate of the ANTLR->Superpower migration (see
/// C:\Users\theor\.claude\plans\stateful-dancing-stroustrup.md): the new Superpower-based
/// AstVisitor/StoryParser must produce behaviorally identical results to the frozen ANTLR path
/// (Moirai.Parser.Antlr, referenced via `extern alias` since both share the `Moirai.Parser`
/// namespace) -- same printed output, same error count, same simulation outcome.
public class ParserDifferentialTests : TestsBase
{
    static IEnumerable<string> CorpusFiles()
    {
        yield return "../../../../MoiraiCli/w.sg";
        yield return "../../../../MoiraiCli/space.sg";
        yield return "../../../../MoiraiWebServer/wwwroot/space.sg";
    }

    [Test]
    [TestCaseSource(nameof(CorpusFiles))]
    public void CorpusFile_MatchesAntlrPath_PrintAndErrors(string rpath)
    {
        var path = Path.GetFullPath(rpath);
        if (!File.Exists(path))
            Assert.Inconclusive(path);
        var content = File.ReadAllText(path);

        var oursDb = StoryParser.Parse(content, out var ourErrors);
        var antlrDb = AntlrStoryParser.Parse(content, out var antlrErrors);

        Assert.That(ourErrors.Count, Is.EqualTo(antlrErrors.Count),
            "error count differs:\nours: " + string.Join("\n", ourErrors) +
            "\nantlr: " + string.Join("\n", antlrErrors));

        var oursPrinted = oursDb.Printer.Print();
        var antlrPrinted = antlrDb.Printer.Print();
        Assert.That(oursPrinted, Is.EqualTo(antlrPrinted));
    }

    [Test]
    [TestCaseSource(nameof(CorpusFiles))]
    public void CorpusFile_MatchesAntlrPath_PassYearsDeterministic(string rpath)
    {
        var path = Path.GetFullPath(rpath);
        if (!File.Exists(path))
            Assert.Inconclusive(path);
        var content = File.ReadAllText(path);

        var oursDb = StoryParser.Parse(content, out _);
        oursDb.Init();
        oursDb.Ctx.PassYears(50, true);
        var oursRecords = oursDb.Records.Select(r => r.Text).ToList();

        var antlrDb = AntlrStoryParser.Parse(content, out _);
        antlrDb.Init();
        antlrDb.Ctx.PassYears(50, true);
        var antlrRecords = antlrDb.Records.Select(r => r.Text).ToList();

        Assert.That(oursRecords, Is.EqualTo(antlrRecords));
    }
}
