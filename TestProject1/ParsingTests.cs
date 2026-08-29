using System.Linq;
using Moirai.Parser;

namespace TestProject1;

public class ParsingTests : TestsBase
{
    static IEnumerable<string> GetFilePaths()
    {
        yield return "../../../../MoiraiCli/w.sg";
        yield return "../../../../MoiraiWebServer/wwwroot/space.sg";
    }
    [Test]
    [TestCaseSource(nameof(GetFilePaths))]
    public void ParseWholeFile(string rpath)
    {
        var path = Path.GetFullPath(rpath);
        Console.WriteLine(path);
        if(!File.Exists(path))
            Assert.Inconclusive(path);
        var db = StoryParser.Parse(File.ReadAllText(path), out var errors);
        Console.WriteLine("------------------");
        var record = db.Printer.Print();
        Console.WriteLine(record);
        Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        StoryParser.Parse(record, out var errors2);
        Assert.AreEqual(0, errors2.Count, string.Join("\n", errors2));
        db.Init();
        db.Ctx.PassYears(100, true);
        db.Commit();
    }
    // Originally ParseSpaceAwareFile/ParseSpaceAware, which hand-rolled ANTLR directly (CodePointCharStream
    // + moirai_lexer + MoiraiParser) and walked every terminal node with a TestVisitor that printed
    // LINE_BREAK specially and dumped each token's leading/trailing COMMENTS-channel hidden tokens via
    // GetHiddenTokensToLeft/Right -- a pure print, no assertions. Retargeted (Phase 5 of the migration,
    // see .claude/plans/stateful-dancing-stroustrup.md) at MoiraiTokenizer's trivia model, which answers
    // the same question (are LINE_BREAK/COMMENT visible, and can a token's neighboring comment be found)
    // directly and assertably instead of by eye.
    [Test]
    [TestCaseSource(nameof(GetFilePaths))]
    public void TokenizesWithLineBreaksAndCommentsVisible(string rpath)
    {
        var path = Path.GetFullPath(rpath);
        if (!File.Exists(path))
            Assert.Inconclusive();
        var content = File.ReadAllText(path);
        var result = MoiraiTokenizer.Tokenize(content);
        Assert.That(result.Errors, Is.Empty, string.Join("\n", result.Errors.Select(e => e.Message)));

        var full = result.FullTokens.ToArray();
        // LINE_BREAK and COMMENT are real, visible tokens in the full stream (not silently discarded
        // the way SPACE is), matching how the ANTLR grammar routed COMMENT to its own COMMENTS channel
        // rather than HIDDEN.
        Assert.That(full.Select(t => t.Kind), Does.Contain(MoiraiTokenKind.LineBreak));
        Assert.That(full.Select(t => t.Kind), Does.Contain(MoiraiTokenKind.Comment));

        // Every parse-token maps back to its real position in the full stream, so comment/whitespace
        // association (what the old test's GetHiddenTokensToLeft/Right dump was inspecting) can always
        // be recovered without re-tokenizing.
        var parse = result.ParseTokens.ToArray();
        for (int i = 0; i < parse.Length; i++)
            Assert.That(full[result.ParseIndexToFullIndex[i]].Kind, Is.EqualTo(parse[i].Kind));
    }

    [Test]
    public void CommentsAssociateWithNeighboringTokens()
    {
        const string content = @"event asd {}
// @1 per 1 year
";
        var result = MoiraiTokenizer.Tokenize(content);
        Assert.That(result.Errors, Is.Empty);

        var kinds = result.FullTokens.Select(t => t.Kind).ToArray();
        Assert.That(kinds, Is.EqualTo(new[]
        {
            MoiraiTokenKind.Event, MoiraiTokenKind.Space, MoiraiTokenKind.Id, MoiraiTokenKind.Space,
            MoiraiTokenKind.ScopeOpen, MoiraiTokenKind.ScopeClose,
            MoiraiTokenKind.LineBreak, MoiraiTokenKind.Comment, MoiraiTokenKind.LineBreak,
        }));

        var commentIndex = Array.IndexOf(kinds, MoiraiTokenKind.Comment);
        var comment = result.FullTokens.ToArray()[commentIndex];
        Assert.That(comment.ToStringValue(), Is.EqualTo("// @1 per 1 year"));
        // The comment sits between the two line breaks -- trailing the '}' line, leading the next.
        Assert.That(kinds[commentIndex - 1], Is.EqualTo(MoiraiTokenKind.LineBreak));
        Assert.That(kinds[commentIndex + 1], Is.EqualTo(MoiraiTokenKind.LineBreak));
    }

    private const string PersonEntity = @"
entity Person {
prop alive: bool
}
";

    [Test]
    public void RedundantTypeFilter_TypedEach_Warns()
    {
        StoryParser.Parse(PersonEntity + @"
event e {
    each Person $p: (type = Person, alive = true) {
        record ''
    }
}", out var errors);

        Assert.That(errors.Where(e => e.Severity == StoryParser.Severity.Error), Is.Empty,
            string.Join("\n", errors));
        var warnings = errors.Where(e => e.Code == StoryParser.ErrorCode.RedundantTypeFilter).ToList();
        Assert.That(warnings, Has.Count.EqualTo(1), string.Join("\n", errors));
        Assert.That(warnings[0].Severity, Is.EqualTo(StoryParser.Severity.Warning));
    }

    [Test]
    public void RedundantTypeFilter_TypedPick_Warns()
    {
        StoryParser.Parse(PersonEntity + @"
event e {
    pick Person $p: (type = Person, alive = true)
}", out var errors);

        Assert.That(errors.Count(e => e.Code == StoryParser.ErrorCode.RedundantTypeFilter), Is.EqualTo(1),
            string.Join("\n", errors));
    }

    [Test]
    public void RedundantTypeFilter_NoTypeFilter_DoesNotWarn()
    {
        StoryParser.Parse(PersonEntity + @"
event e {
    each Person $p: (alive = true) {
        record ''
    }
}", out var errors);

        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    [Test]
    public void RedundantTypeFilter_DifferentType_DoesNotWarn()
    {
        // `type = Item` inside a `Person` iteration is a (different) contradiction, not the redundant
        // self-type case, so it must not be flagged by the redundancy lint.
        StoryParser.Parse(@"
entity Person {
prop alive: bool
}
entity Item {
}
event e {
    each Person $p: (type = Item, alive = true) {
        record ''
    }
}", out var errors);

        Assert.That(errors.Count(e => e.Code == StoryParser.ErrorCode.RedundantTypeFilter), Is.EqualTo(0),
            string.Join("\n", errors));
    }
}
