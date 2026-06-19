using Moirai.Parser;

namespace TestProject1;

public class SingletonTests : TestsBase
{
    private const string Story = @"
singleton World {
    prop turn: number
}
event setup {
    create World $w: 'w' {
        turn := 5
    }
    record('turn={#World.turn}')
}";

    [Test]
    public void SingletonReadAndCache()
    {
        var db = Run(Story, out _, 0);     // parses + round-trips through the printer + Init()
        db.RunAction(db.Actions[0]);

        // #World.turn resolves the singleton instance.
        Assert.That(db.Records.Single().Text, Is.EqualTo("turn=5"));

        var world = db.GetEntityType("World");
        Assert.That(world.IsSingleton, Is.True, "the singleton keyword sets IsSingleton");
        Assert.That(db.TryGetSingleton(world.Id, out var id), Is.True, "instance is cached for O(1) lookup");
        Assert.That(id.Id, Is.Not.EqualTo(0u));
    }

    [Test]
    public void PrintsSingletonKeyword()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        Assert.That(db.Printer.Print(), Does.Contain("singleton World"));
    }
}
