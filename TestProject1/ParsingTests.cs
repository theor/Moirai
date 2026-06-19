using System.Collections.ObjectModel;
using System.Linq;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Moirai.Parser;

namespace TestProject1;

public class ParsingTests : TestsBase
{
    static IEnumerable<string> GetFilePaths()
    {
        yield return "../../../../MoiraiWebServer/wwwroot/w.sg";
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
    [Test]
    [TestCaseSource(nameof(GetFilePaths))]
    public void ParseSpaceAwareFile(string rpath)
    {
        var path = Path.GetFullPath(rpath);
        Console.WriteLine(path);
        if(!File.Exists(path))
            Assert.Inconclusive();
        var content = File.ReadAllText(path);
        var fromString = new CodePointCharStream(content /*.TrimStart('\r', '\n', ' ')*/);
        var lexer = new moirai_lexer(fromString);
        var tokens = /*mergeChannels ? new BufferedTokenStream(lexer) :*/ new CommonTokenStream(lexer);
        var parser = new MoiraiParser(tokens);
        var r = parser.r();
        r.Accept(new TestVisitor(){Parser = parser, Lexer = lexer, Stream = fromString});
    }
    [Test]
    [TestCase(@"event asd {}
// @1 per 1 year
")]
    public void ParseSpaceAware(string content)
    {
        var fromString = new CodePointCharStream(content /*.TrimStart('\r', '\n', ' ')*/);
        var lexer = new moirai_lexer(fromString);
        var tokens = /*mergeChannels ? new BufferedTokenStream(lexer) :*/ new CommonTokenStream(lexer);
        var parser = new MoiraiParser(tokens);
        var r = parser.r();
        r.Accept(new TestVisitor(){Parser = parser, Lexer = lexer, Stream = fromString});
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

    class TestVisitor : MoiraiParserBaseVisitor<object?>, StoryParser.IVisitor
    {
        public List<StoryParser.Error> Errors { get; } = new();
        public MoiraiParser Parser { get; set; }
        public (int offsetLine, int offsetColumn) Offset { get; set; }
        public moirai_lexer Lexer { get; set; }
        public CodePointCharStream Stream { get; set; }

        public override object? VisitTerminal(ITerminalNode node)
        {
            Console.WriteLine($"T: '{(node.Symbol.Type == moirai_lexer.LINE_BREAK ? "LINE_BREAK" : node.GetText())}'");
            var hidden = ((CommonTokenStream)Parser.TokenStream).GetHiddenTokensToLeft(node.Symbol.TokenIndex, moirai_lexer.COMMENTS) ?? ReadOnlyCollection<IToken>.Empty;
            Console.WriteLine("  L: " + string.Join("|",hidden.Select(t => $"'{t.Text}'")));
            hidden = ((CommonTokenStream)Parser.TokenStream).GetHiddenTokensToRight(node.Symbol.TokenIndex,
                moirai_lexer.COMMENTS) ?? ReadOnlyCollection<IToken>.Empty;
            Console.WriteLine("  R: " + string.Join("|",hidden.Select(t => $"'{t.Text}'")));

            return base.VisitTerminal(node);
        }
    }

}
