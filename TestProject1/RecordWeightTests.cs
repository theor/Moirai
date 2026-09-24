using Moirai.Parser;

namespace TestProject1;

/// record('text', weight): the optional weight a chronicle ranks records by. It is metadata only, so the
/// story's behaviour must not move when weights are added.
public class RecordWeightTests : TestsBase
{
    private const string Story = @"
entity Person {
    prop rank: number
}
event e {
    create Person $p {
        rank := 3
    }
    record('plain')
    record('heavy', 4)
    record('noise', 0)
    record('by rank', $p.rank + 1)
    record 'bare'
}
";

    [Test]
    public void WeightDefaultsToOneAndCanBeAnyNumberExpression()
    {
        var db = Run(Story, out _, s => Assert.That(s, Does.Contain("record ('heavy', 4)")));
        db.RunAction(db.Actions[0]);

        var weights = db.Records.ToDictionary(r => r.Text, r => r.Weight);
        Assert.That(weights["plain"], Is.EqualTo(Database.Record.DefaultWeight));
        Assert.That(weights["heavy"], Is.EqualTo(4));
        Assert.That(weights["noise"], Is.EqualTo(0));
        Assert.That(weights["by rank"], Is.EqualTo(4));
        Assert.That(weights["bare"], Is.EqualTo(Database.Record.DefaultWeight));
    }

    [TestCase("record('x', 'heavy')")]
    [TestCase("record('x', true)")]
    [TestCase("record('x', 1, 2)")]
    public void AWeightThatIsNotOneNumberIsAnError(string call)
    {
        StoryParser.Parse($"event e {{\n    {call}\n}}\n", out var errors);
        Assert.That(errors.Select(e => e.Code),
            Has.Some.EqualTo(StoryParser.ErrorCode.InvalidArgument).Or.Some.EqualTo(StoryParser.ErrorCode.MissingArgument));
    }
}
