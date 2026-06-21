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

    [Test]
    public void MonarchsAreCrownedAndSucceeded()
    {
        var story = File.ReadAllText(FindWsg());
        var db = StoryParser.Parse(story, out _);
        db.History = new();
        db.Init();
        db.Ctx.PassYears(400, true);

        var crownings = db.Records.Count(r => r.Text.Contains("is crowned ruler of"));
        var successions = db.Records.Count(r => r.Text.Contains("succeeds"));
        var vacancies = db.Records.Count(r => r.Text.Contains("is left vacant"));

        Assert.That(crownings, Is.GreaterThan(0), "adults should be crowned over a 400-year run");
        // Monarchs die over the centuries, each death passing the crown to an heir or vacating it.
        Assert.That(successions, Is.GreaterThan(0), "heirs should succeed dead monarchs");
        Assert.That(vacancies, Is.GreaterThan(0), "some heirless deaths should vacate a throne");

        // Invariant: no realm may keep a dead person on the throne — succession must transfer or vacate.
        var countryType = db.GetEntityType("Country");
        var rulerProp = countryType.GetPropertyId("ruler");
        var personType = db.GetEntityType("Person");
        var titleProp = personType.GetPropertyId("title");
        var aliveProp = personType.GetPropertyId("alive");

        int ruled = 0;
        foreach (var e in db.Entities)
        {
            if (e.Type != countryType.Id) continue;
            var ruler = e.GetProperty(rulerProp);
            if (ruler.Id.IsNull) continue;
            Assert.That(db.TryGetEntity(ruler.Id, out var king), Is.True);
            // Title enum is { Commoner=1, King=2 }; a sitting ruler must be a living King.
            Assert.That(king.GetProperty(titleProp).IntValue, Is.EqualTo(2),
                "a country's ruler must hold the King title");
            Assert.That(king.GetProperty(aliveProp).BoolValue, Is.True,
                "a sitting ruler must be alive (no corpse on the throne)");
            ruled++;
        }

        Assert.That(ruled, Is.GreaterThan(0), "at least one realm should have a reigning monarch");
    }

    [Test]
    public void SettlementsAreFoundedGrowAndFallToRuin()
    {
        var story = File.ReadAllText(FindWsg());
        var db = StoryParser.Parse(story, out _);
        db.History = new();
        db.Init();
        db.Ctx.PassYears(400, true);

        var founded = db.Records.Count(r => r.Text.Contains("founds the village of"));
        var grewTown = db.Records.Count(r => r.Text.Contains("grows into a town"));
        var grewCity = db.Records.Count(r => r.Text.Contains("grows into a city"));
        var ruined = db.Records.Count(r => r.Text.Contains("falls into ruin"));

        var settlementType = db.GetEntityType("Settlement");
        int total = 0;
        foreach (var e in db.Entities)
            if (e.Type == settlementType.Id) total++;

        Assert.That(founded, Is.GreaterThan(0), "settlements should be founded over a 400-year run");
        Assert.That(grewTown, Is.GreaterThan(0), "some villages should grow into towns");
        Assert.That(grewCity, Is.GreaterThan(0), "some towns should grow into cities");
        Assert.That(ruined, Is.GreaterThan(0), "war should reduce some settlements to ruins");
        Assert.That(total, Is.GreaterThan(0));
    }

    [Test]
    public void WizardsAdvanceAndForgeArtifacts()
    {
        var story = File.ReadAllText(FindWsg());
        var db = StoryParser.Parse(story, out _);
        db.History = new();
        db.Init();
        db.Ctx.PassYears(400, true);

        var legendary = db.Records.Count(r => r.Text.Contains("attains legendary mastery"));
        var masters = db.Records.Count(r => r.Text.Contains("becomes a master wizard"));
        var forged = db.Records.Count(r => r.Text.Contains("forges the enchanted") || r.Text.Contains("forges the cursed"));
        var catastrophes = db.Records.Count(r => r.Text.Contains("goes catastrophically wrong"));

        Assert.That(masters, Is.GreaterThan(0), "wizards should climb to master rank");
        Assert.That(legendary, Is.GreaterThan(0), "a wizard should reach the (previously unreachable) Legendary mastery");
        Assert.That(forged, Is.GreaterThan(0), "master wizards should forge enchanted artifacts");

        // Enchanted artifacts must be real Items flagged enchanted with a power set.
        var itemType = db.GetEntityType("Item");
        var enchantedProp = itemType.GetPropertyId("enchanted");
        var powerProp = itemType.GetPropertyId("power");
        int enchantedItems = 0;
        foreach (var e in db.Entities)
        {
            if (e.Type != itemType.Id) continue;
            if (!e.GetProperty(enchantedProp).BoolValue) continue;
            enchantedItems++;
            Assert.That(e.GetProperty(powerProp).IntValue, Is.GreaterThan(0),
                "an enchanted artifact must carry an ArtifactPower");
        }

        Assert.That(enchantedItems, Is.GreaterThan(0), "enchanted artifacts should exist in the world");
    }

    [Test]
    public void FaithProducesMiraclesProphetsAndSaints()
    {
        var story = File.ReadAllText(FindWsg());
        var db = StoryParser.Parse(story, out _);
        db.History = new();
        db.Init();
        db.Ctx.PassYears(400, true);

        var miracles = db.Records.Count(r => r.Text.Contains("receives a miracle from"));
        var temples = db.Records.Count(r => r.Text.Contains("raises a temple in"));
        var saints = db.Records.Count(r => r.Text.Contains("is canonized as a saint"));

        Assert.That(miracles, Is.GreaterThan(0), "devout believers in crisis should receive miracles");
        Assert.That(temples, Is.GreaterThan(0), "prophets should raise temples");
        Assert.That(saints, Is.GreaterThan(0), "deeply devout believers should be canonized on death");

        // Temples exist as entities tied to a god; some person carries the saint flag.
        var templeType = db.GetEntityType("Temple");
        int templeEntities = db.Entities.Count(e => e.Type == templeType.Id);
        Assert.That(templeEntities, Is.GreaterThan(0), "temple entities should exist");

        var personType = db.GetEntityType("Person");
        var saintProp = personType.GetPropertyId("is_saint");
        int saintEntities = db.Entities.Count(e => e.Type == personType.Id && e.GetProperty(saintProp).BoolValue);
        Assert.That(saintEntities, Is.GreaterThan(0), "at least one canonized saint should exist");
    }

    [Test]
    public void FactionsTakeOnKindsAndFeud()
    {
        var story = File.ReadAllText(FindWsg());
        var db = StoryParser.Parse(story, out _);
        db.History = new();
        db.Init();
        db.Ctx.PassYears(400, true);

        var circles = db.Records.Count(r => r.Text.Contains("founds the mage circle"));
        var orders = db.Records.Count(r => r.Text.Contains("founds the knightly order"));
        var guilds = db.Records.Count(r => r.Text.Contains("founds the thieves guild"));
        var cults = db.Records.Count(r => r.Text.Contains("founds the cult"));
        var feuds = db.Records.Count(r => r.Text.Contains("strikes down") && r.Text.Contains(" of "));

        // A faction's kind comes from its founder's calling, so several kinds should appear.
        int kindsSeen = new[] { circles, orders, guilds, cults }.Count(c => c > 0);
        Assert.That(kindsSeen, Is.GreaterThanOrEqualTo(3), "factions of several callings should be founded");
        Assert.That(feuds, Is.GreaterThan(0), "knightly orders and thieves guilds should feud");

        // Factions carry a non-default kind on the entity.
        var factionType = db.GetEntityType("Faction");
        var kindProp = factionType.GetPropertyId("kind");
        int withKind = db.Entities.Count(e => e.Type == factionType.Id && e.GetProperty(kindProp).IntValue > 0);
        Assert.That(withKind, Is.GreaterThan(0), "factions should record a FactionKind");
    }
}
