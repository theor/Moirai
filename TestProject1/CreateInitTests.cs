using Moirai.Parser;

namespace TestProject1;

public class CreateInitTests : TestsBase
{
    private const string Story = @"
entity Thing {
    prop a: number
    prop b: number
}
event setup {
    create Thing $t: 'x' {
        a := 5
        b := 6 + 1
    }
    record('a={$t.a} b={$t.b}')
}";

    [Test]
    public void ObjectInitializerBlock()
    {
        var db = Run(Story, out _, 0);   // parses + round-trips through the printer
        db.RunAction(db.Actions[0]);
        // The block initialized the new entity, and $t still resolves after the block.
        // ({$t.a} renders the value as an entity link, e.g. "a=<#1>5</>".)
        var rec = db.Records.Single().Text;
        Assert.That(rec, Does.Contain(">5</>"), rec);   // a := 5
        Assert.That(rec, Does.Contain(">7</>"), rec);   // b := 6 + 1
    }

    [Test]
    public void RoundTripsAsColonEquals()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty);
        var printed = db.Printer.Print();
        Assert.That(printed, Does.Contain("a := 5"), "the := initializer form must be preserved by the printer");
        var db2 = StoryParser.Parse(printed, out var errors2);
        Assert.That(errors2, Is.Empty, string.Join("\n", errors2));
        Assert.That(db2.Printer.Print(), Is.EqualTo(printed), "printer must be idempotent");
    }
}
