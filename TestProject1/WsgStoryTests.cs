using Moirai.Parser;

namespace TestProject1;

// Guards the canonical sample story MoiraiCli/w.sg: it must keep parsing and running as the grammar
// and engine evolve. Also exercises the dynastic-name feature (Surname table + family_name inheritance).
public class WsgStoryTests
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

    [Test]
    public void ParsesAndSimulatesWithoutErrors()
    {
        var story = File.ReadAllText(FindWsg());
        var db = StoryParser.Parse(story, out var errors);
        Assert.That(errors.Count(e => e.Severity == StoryParser.Severity.Error), Is.EqualTo(0),
            string.Join("\n", errors));

        db.History = new();
        db.Init();
        db.Ctx.PassYears(120, true);
        Assert.That(db.Records.Count, Is.GreaterThan(0), "a 120-year run should produce narrative records");
    }

    [Test]
    public void ChildrenInheritTheirFathersHouse()
    {
        var story = File.ReadAllText(FindWsg());
        var db = StoryParser.Parse(story, out _);
        db.History = new();
        db.Init();
        db.Ctx.PassYears(200, true);

        var personType = db.GetEntityType("Person");
        var familyNameProp = personType.GetPropertyId("family_name");
        var parent1Prop = personType.GetPropertyId("parent1");

        // Find any person with a father; their family_name must match the father's (dynastic surname).
        int checkedPairs = 0;
        foreach (var e in db.Entities)
        {
            if (e.Type != personType.Id) continue;
            var p1 = e.GetProperty(parent1Prop);
            if (p1.Id.IsNull) continue;
            if (!db.TryGetEntity(p1.Id, out var father)) continue;

            var childHouse = e.GetProperty(familyNameProp).Value;
            var fatherHouse = father.GetProperty(familyNameProp).Value;
            if (string.IsNullOrEmpty(fatherHouse)) continue;

            Assert.That(childHouse, Is.EqualTo(fatherHouse),
                "a child's family_name should equal their parent1 (father)'s house");
            checkedPairs++;
            if (checkedPairs >= 20) break;
        }

        Assert.That(checkedPairs, Is.GreaterThan(0),
            "a 200-year run should produce at least one parent-child pair to verify inheritance");
    }
}
