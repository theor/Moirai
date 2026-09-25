using Moirai.Parser;

namespace TestProject1;

/// A pick or each visits only the entities its predicate could match -- an equality index on reference
/// and enum properties, and a user function's body read through its arguments -- but it must match
/// exactly what a scan of the whole type would, in the same ascending id order (a pick's RNG draws depend
/// on it). Each case records what it matched, so a narrowing that loses or reorders an entity shows up.
public class QueryNarrowingTests
{
    private const string Story = @"
enum Job { None, Smith, Farmer }
entity Place {}
entity P {
    prop place: Place
    prop job: Job
    prop parent1: P
    prop parent2: P
}
function child_of($c: P, $p: P): bool {
    $c.parent1 = $p or $c.parent2 = $p
}
@start
event setup {
    create Place $a: 'a'
    create Place $b: 'b'
    create P $x: 'x' {
        place := $a
        job := Job.Smith
    }
    create P $y: 'y' {
        place := $b
        job := Job.Farmer
    }
    create P $z: 'z' {
        parent1 := $x
        job := Job.None
    }
    create P $w: 'w' {
        parent2 := $x
    }
    create P $v: 'v' {
        parent1 := $y
    }
}
event at_a {
    pick Place $a: (name = 'a')
    each P $p: (place = $a) {
        record('{$p.name}')
    }
}
event at_b {
    pick Place $b: (name = 'b')
    each P $p: (place = $b) {
        record('{$p.name}')
    }
}
event smiths {
    each P $p: (job = Job.Smith) {
        record('{$p.name}')
    }
}
event no_job {
    each P $p: (job = Job.None) {
        record('{$p.name}')
    }
}
event children_of_x {
    pick P $x: (name = 'x')
    each P $c: (child_of($c, $x)) {
        record('{$c.name}')
    }
}
event parents_of_z {
    pick P $z: (name = 'z')
    each P $p: (child_of($z, $p)) {
        record('{$p.name}')
    }
}
event move_x {
    pick P $x: (name = 'x')
    pick Place $b: (name = 'b')
    set $x.place = $b
}
";

    private static Database World()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors.Select(e => e.Message)));
        db.Init();
        return db;
    }

    private static string[] Run(Database db, string rule)
    {
        var before = db.Records.Count;
        db.RunAction(rule);
        return db.Records.Skip(before).Select(r => r.Text.Split('>')[1].Split('<')[0]).ToArray();
    }

    [Test]
    public void AReferenceEqualityFindsExactlyItsHolders() =>
        Assert.That(Run(World(), "at_a"), Is.EqualTo(new[] { "x" }));

    [Test]
    public void AnEnumEqualityFindsExactlyItsHolders() =>
        Assert.That(Run(World(), "smiths"), Is.EqualTo(new[] { "x" }));

    [Test]
    public void EqualityToZeroIsLeftToTheScan()
    {
        // Job.None is 0, a value the buckets cannot vouch for, so this one scans -- and gives exactly
        // what the scan always gave: only the entity that set it. (An unset enum is not its first member;
        // that is the language's rule, unchanged by the index.)
        Assert.That(Run(World(), "no_job"), Is.EqualTo(new[] { "z" }));
    }

    [Test]
    public void AFunctionWithAnOrIsReadThroughItsArguments()
    {
        var db = World();
        Assert.That(Run(db, "children_of_x"), Is.EqualTo(new[] { "z", "w" }), "the scanned variable's own properties");
        Assert.That(Run(db, "parents_of_z"), Is.EqualTo(new[] { "x" }), "the scanned variable itself");
    }

    [Test]
    public void TheIndexFollowsAChangedProperty()
    {
        var db = World();
        db.RunAction("move_x");
        Assert.That(Run(db, "at_a"), Is.Empty);
        Assert.That(Run(db, "at_b"), Is.EqualTo(new[] { "x", "y" }), "ascending id order, as a scan gives");
    }
}
