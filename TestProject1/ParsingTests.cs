using Antlr4.Runtime;
using Moirai.Parser;

namespace TestProject1;

public class ParsingTests : TestsBase

{

    [Test]
    [TestCase("../../../../MoiraiCli/w.sg")]
    [TestCase("../../../../MoiraiCli/space.sg")]
    public void ParseWholeFile(string rpath)
    {
        var path = Path.GetFullPath(rpath);
        Console.WriteLine(path);
        Assert.IsTrue(File.Exists(path));
        var db = StoryParser.Parse(File.ReadAllText(path), out var errors);
        Console.WriteLine("------------------");
        var record = db.Printer.Print();
        Console.WriteLine(record);
        Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        var db2 = StoryParser.Parse(record, out var errors2);
        Assert.AreEqual(0, errors2.Count, string.Join("\n", errors2));
        db.Init();
        db.Ctx.PassYears(100, true);
        db.Commit();
    }
    [Test]
    // [TestCase("../../../../MoiraiCli/w.sg")]
    [TestCase("../../../../MoiraiCli/space.sg")]
    public void ParseSpaceAware(string rpath)
    {
        var path = Path.GetFullPath(rpath);
        Console.WriteLine(path);
        Assert.IsTrue(File.Exists(path));
        var content = File.ReadAllText(path);
        var fromString = new CodePointCharStream(content /*.TrimStart('\r', '\n', ' ')*/);
        var lexer = new moirai_lexer(fromString);
        var tokens = /*mergeChannels ? new BufferedTokenStream(lexer) :*/ new CommonTokenStream(lexer);
        var parser = new MoiraiParser(tokens);
        var r = parser.r();
        r.Accept(new TestVisitor(){Parser = parser, Lexer = lexer, Stream = fromString});
    }

    class TestVisitor : MoiraiParserBaseVisitor<object?>, StoryParser.IVisitor
    {
        public List<StoryParser.Error> Errors { get; } = new();
        public MoiraiParser Parser { get; set; }
        public (int offsetLine, int offsetColumn) offset { get; set; }
        public moirai_lexer Lexer { get; set; }
        public CodePointCharStream Stream { get; set; }

        public override object? VisitProp_definition(MoiraiParser.Prop_definitionContext context)
        {
            return base.VisitProp_definition(context);
        }
    }

}
