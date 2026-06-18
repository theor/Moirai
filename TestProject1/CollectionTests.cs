using Moirai.Parser;

namespace TestProject1;

public class CollectionTests : TestsBase
{
    // Exercises the full collection surface in one @start event so locals stay in scope:
    // add (with dedupe -> set semantics), count (in interpolation, in-memory Compute),
    // contains (in `if`, in-memory) and (in `each`, SQL EXISTS), and remove.
    private const string Story = @"
entity Person {
    prop parents: [Person]
}
event setup {
    create Person $a: 'a'
    create Person $b: 'b'
    create Person $c: 'c'
    add($c.parents, $a)
    add($c.parents, $b)
    add($c.parents, $a)
    record('count={count($c.parents)}')
    if contains($c.parents, $a) {
        record('has a')
    }
    if contains($c.parents, $c) {
        record('has c')
    } else {
        record('no c')
    }
    each Person $child: (contains($child.parents, $a)) {
        record('child of a: {$child.name}')
    }
    remove($c.parents, $a)
    record('after remove={count($c.parents)}')
}";

    // The rewritten w.sg uses [Person] collections and add/remove/contains/count; verify it parses
    // with no errors and round-trips through the printer (parse -> print -> reparse -> print is stable).
    [Test]
    public void WsgRoundTrips()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "MoiraiCli", "w.sg");
        var db = StoryParser.Parse(File.ReadAllText(path), out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        var printed = db.Printer.Print();
        var db2 = StoryParser.Parse(printed, out var errors2);
        Assert.That(errors2, Is.Empty, "reparse errors:\n" + string.Join("\n", errors2));
        Assert.That(db2.Printer.Print(), Is.EqualTo(printed), "printer must be idempotent");
        Assert.That(printed, Does.Contain("prop parents: [Person]"));
        Assert.That(printed, Does.Contain("contains"));
    }

    [Test]
    public void AddContainsCountRemove()
    {
        var db = Run(Story, out _);          // parses, round-trips through the printer, Init()
        db.RunAction(db.Actions[0]);          // run @start setup
        var records = db.Records.Select(r => r.Text).ToList();
        foreach (var r in records) Console.WriteLine(r);

        // {$child.name} renders as an entity link "<#N>c</>", so match on prefix + name.
        var childRecords = records.Where(r => r.StartsWith("child of a:")).ToList();

        Assert.That(records, Does.Contain("count=2"), "double add of $a must dedupe (set semantics)");
        Assert.That(records, Does.Contain("has a"), "in-memory contains true");
        Assert.That(records, Does.Contain("no c"), "in-memory contains false");
        Assert.That(childRecords.Count, Is.EqualTo(1), "SQL each+contains must find exactly the child of a");
        Assert.That(childRecords[0], Does.Contain(">c</>"), "the one child of a is c");
        Assert.That(records, Does.Contain("after remove=1"), "remove must drop one member");
    }
}
