using Moirai.Parser;

namespace TestProject1;

// Named weighted tables: `table Name { w => value, ... }` sampled with roll(Name).
// Note: `Time` is a builtin type, so stories must not redeclare it.
public class TableTests : TestsBase
{
    private const string Story = @"
enum Job { Farmer, Merchant, Noble }
table Occupation { 70 => Job.Farmer, 20 => Job.Merchant, 10 => Job.Noble }
table Title { 'the Bold', 'the Wise', 'the Cruel' }
event roll_job {
    record('{roll(Occupation)}')
}
event roll_title {
    record('{roll(Title)}')
}";

    [Test]
    public void ParsesAndRoundTrips()
    {
        // Run() parses, prints, reparses, and asserts the two prints are identical — so this also
        // proves table_definition survives the StoryPrinter round-trip.
        var db = Run(Story, out _, 0);
        Assert.That(db.Tables.Count, Is.EqualTo(3), "two declared tables + the index-0 sentinel");
        Assert.That(db.GetTableDefinition("Occupation", out var occ), Is.True);
        Assert.That(occ!.TotalWeight, Is.EqualTo(100));
    }

    [Test]
    public void WeightedRollFavorsHeaviestEntry()
    {
        var db = Run(Story, out _, 0);
        var spawn = db.Actions.Single(a => a.Name == "roll_job");
        for (int i = 0; i < 600; i++)
            db.RunAction(spawn);

        var counts = db.Records
            .GroupBy(r => r.Text)
            .ToDictionary(g => g.Key, g => g.Count());

        // Every result must be one of the table's enum values.
        Assert.That(counts.Keys, Is.SubsetOf(new[] { "Farmer", "Merchant", "Noble" }));
        // The 70-weight entry dominates the 20- and 10-weight ones.
        Assert.That(counts.GetValueOrDefault("Farmer"),
            Is.GreaterThan(counts.GetValueOrDefault("Merchant")));
        Assert.That(counts.GetValueOrDefault("Merchant"),
            Is.GreaterThan(counts.GetValueOrDefault("Noble")));
        Assert.That(counts.GetValueOrDefault("Farmer"), Is.GreaterThan(300),
            "Farmer ~70% of 600 should clear 300 by a wide margin");
    }

    [Test]
    public void StringTableEqualWeightStaysInSet()
    {
        var db = Run(Story, out _, 0);
        var rollTitle = db.Actions.Single(a => a.Name == "roll_title");
        for (int i = 0; i < 200; i++)
            db.RunAction(rollTitle);

        var titles = db.Records.Select(r => r.Text).Distinct().ToHashSet();
        Assert.That(titles, Is.SubsetOf(new[] { "the Bold", "the Wise", "the Cruel" }));
        // Equal weights over 200 draws should surface all three.
        Assert.That(titles.Count, Is.EqualTo(3));
    }

    [Test]
    public void DeterministicPerSeed()
    {
        // Same story, same default seed => identical roll sequence.
        var a = Run(Story, out _, 0);
        var b = Run(Story, out _, 0);
        var spawnA = a.Actions.Single(x => x.Name == "roll_job");
        var spawnB = b.Actions.Single(x => x.Name == "roll_job");
        for (int i = 0; i < 50; i++)
        {
            a.RunAction(spawnA);
            b.RunAction(spawnB);
        }

        Assert.That(a.Records.Select(r => r.Text),
            Is.EqualTo(b.Records.Select(r => r.Text)).AsCollection);
    }

    [Test]
    public void UnknownTableIsAnError()
    {
        const string s = @"
event bad {
    record('{roll(DoesNotExist)}')
}";
        // Parse directly: an unresolved roll() also cascades a downstream error, so we assert the
        // specific UnknownTable diagnostic is present rather than pinning an exact count.
        StoryParser.Parse(s, out var errors);
        Assert.That(errors.Any(e => e.Code == StoryParser.ErrorCode.UnknownTable));
    }
}
