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

    private const string Chained = @"
entity Place {
    prop size: number
    prop next: Place
}
singleton World {
    prop turn: number
    prop capital: Place
    function doubled(): number {
        $self.turn * 2
    }
}
event setup {
    create Place $far {
        size := 7
    }
    create Place $c {
        size := 3
        next := $far
    }
    create World $w: 'w' {
        turn := 5
        capital := $c
    }
    record('{#World.capital.size} {#World.capital.next.size} {#World.doubled()}')
    if #World.capital.size = 3 {
        record('compared')
    }
}";

    [Test]
    public void ASingletonPathReadsThroughEveryStep()
    {
        // #World.capital.size used to read only the first step and hand back the capital itself.
        var db = Run(Chained, out _, 0);
        db.RunAction("setup");
        var texts = db.Records.Select(r => System.Text.RegularExpressions.Regex.Replace(r.Text, @"<#\d+>(.*?)</>", "$1"));
        Assert.That(texts, Is.EqualTo(new[] { "3 7 10", "compared" }));
    }

    [Test]
    public void PrintsSingletonKeyword()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        Assert.That(db.Printer.Print(), Does.Contain("singleton World"));
    }
}
