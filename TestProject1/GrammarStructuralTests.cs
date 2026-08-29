using Moirai.Parser;
using Moirai.Parser.Ast;

namespace TestProject1;

/// Phase 2 gate of the ANTLR->Superpower migration (see
/// C:\Users\theor\.claude\plans\stateful-dancing-stroustrup.md): the new Superpower combinator
/// grammar (MoiraiGrammar) must parse the full corpus structurally (no semantic actions wired up
/// yet — that's Phase 3's AstVisitor rewrite).
public class GrammarStructuralTests : TestsBase
{
    static IEnumerable<string> CorpusFiles()
    {
        yield return "../../../../MoiraiCli/w.sg";
        yield return "../../../../MoiraiCli/space.sg";
        yield return "../../../../MoiraiWebServer/wwwroot/space.sg";
    }

    static void AssertParsesStructurally(string content)
    {
        var tokenized = MoiraiTokenizer.Tokenize(content);
        Assert.That(tokenized.Errors, Is.Empty,
            "Tokenizer errors: " + string.Join("; ", tokenized.Errors.Select(e => e.Message)));

        var result = MoiraiGrammar.TryParseR(tokenized.ParseTokens);
        if (!result.HasValue)
            Assert.Fail($"Parse failed at {result.ErrorPosition}: {result.ErrorMessage}\n" +
                        $"Expected: {string.Join(" / ", result.Expectations ?? Array.Empty<string>())}\n" +
                        $"Near: {NearText(content, result.ErrorPosition)}");

        if (!result.Remainder.IsAtEnd)
        {
            var next = result.Remainder.ConsumeToken();
            Assert.Fail($"Parser did not consume the whole token stream; stopped at " +
                        $"{next.Value.Position} near: {NearText(content, next.Value.Position)}");
        }
    }

    static string NearText(string content, Superpower.Model.Position pos)
    {
        if (!pos.HasValue) return "(end of input)";
        int start = Math.Max(0, pos.Absolute - 30);
        int len = Math.Min(60, content.Length - start);
        return content.Substring(start, len).Replace("\n", "\\n");
    }

    [Test]
    [TestCaseSource(nameof(CorpusFiles))]
    public void CorpusFile_ParsesStructurally(string rpath)
    {
        var path = Path.GetFullPath(rpath);
        if (!File.Exists(path))
            Assert.Inconclusive(path);
        AssertParsesStructurally(File.ReadAllText(path));
    }
}
