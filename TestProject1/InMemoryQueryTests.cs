using Moirai.Core;
using Moirai.Parser;

namespace TestProject1;

// Guards the in-memory query backend (which replaced the SQLite mirror).
public class InMemoryQueryTests
{
    private static string FindWsg()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "MoiraiCli", "w.sg");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate MoiraiCli/w.sg above " + AppContext.BaseDirectory);
    }

    private static List<string> RunWsg(int years)
    {
        var story = File.ReadAllText(FindWsg());
        var db = StoryParser.Parse(story, out _);
        db.History = new();
        db.Init();
        db.Ctx.PassYears(years, true);
        return db.Records.Select(r => r.Text).ToList();
    }

    // Determinism: the same story + seed must produce a byte-identical record stream on every run.
    [Test]
    public void RunIsDeterministicPerSeed()
    {
        var first = RunWsg(250);
        var second = RunWsg(250);

        Assert.That(second.Count, Is.EqualTo(first.Count), "record counts must match across runs");
        for (int i = 0; i < first.Count; i++)
            Assert.That(second[i], Is.EqualTo(first[i]), $"record #{i} differed between identical runs");
    }

    private const string Genealogy = """
        entity Person {
            prop parent1: Person
            prop parent2: Person
        }

        @start
        event start {
            create Time $t: 'time' {
                year := 0
            }
            create Person $a: 'a'
            create Person $b: 'b'
            create Person $c: 'c' {
                parent1 := $a
                parent2 := $b
            }
        }
        """;

    // The family tree's children query (WorldSession.GetFamilyTree) is a hand-built back-reference: find
    // every entity of a type whose parent1/parent2 points at one id. It runs outside any rule, so its
    // query variable sits in slot 0 of an empty value stack — which is what this pins. Building the
    // same predicate with the retired -1 "no variable" index instead makes FindAll bind the stack at
    // index -1 and throw, and the whole Family page goes blank.
    [Test]
    public void BackReferenceQueryFindsChildrenOutsideARule()
    {
        var db = StoryParser.Parse(Genealogy, out var errors);
        Assert.That(errors.Count(e => e.Severity == StoryParser.Severity.Error), Is.EqualTo(0),
            string.Join("\n", errors));
        db.History = new();
        db.Init();

        var personType = db.Types.First(t => t.Name == "Person");
        var prop1 = personType.GetPropertyId("parent1");
        var prop2 = personType.GetPropertyId("parent2");
        var parentA = db.Entities.First(e => e.Type == personType.Id).Id;

        const int queryVar = 0;
        var results = new List<EntityId>();
        using (db.Ctx.RunScope(true))
        {
            db.FindAll(personType.Id,
                new BinaryOperator(BinaryOperator.Operator.Or,
                    new BinaryOperator(BinaryOperator.Operator.Equals,
                        new PropertyPath(queryVar, personType.RefType, prop1), new Literal(parentA)),
                    new BinaryOperator(BinaryOperator.Operator.Equals,
                        new PropertyPath(queryVar, personType.RefType, prop2), new Literal(parentA))
                ), queryVar, ref results);
        }

        Assert.That(results.Count, Is.EqualTo(1), "'a' has exactly one child");
        Assert.That(db.GetProperty(results[0], Database.PropName, out var name), Is.True);
        Assert.That(name.Value, Is.EqualTo("c"));
    }

    // Quick wall-clock timing. Explicit (run on demand, ideally in Release):
    // dotnet test -c Release --filter "Name~TimeRuns"
    [Test]
    [Explicit]
    public void TimeRuns()
    {
        var story = File.ReadAllText(FindWsg());
        foreach (var years in new[] { 200, 500, 1000 })
        {
            double best = double.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                var db = StoryParser.Parse(story, out _);
                db.History = new();
                db.Init();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                db.Ctx.PassYears(years, true);
                sw.Stop();
                if (i > 0) best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }

            TestContext.Progress.WriteLine($"{years,5} yr: {best,8:F1} ms");
        }
    }
}
