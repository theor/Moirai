using Moirai.Parser;

namespace TestProject1;

// Multi-seed fuzz / invariant harness over MoiraiCli/w.sg.
//
// Why: w.sg drives all randomness through one seeded RNG, so adding or reordering any rule reshuffles
// every downstream draw. Single-seed threshold tests are therefore brittle (a perturbation can flip a
// "happened" to "didn't") AND can hide real bugs that only surface under a different draw order.
//
// This harness splits the two concerns:
//   * InvariantsHold  — seed-INDEPENDENT truths that must hold on EVERY seed. These double as a fuzzer:
//                       running many worlds is how the multi-crown succession bug was found.
//   * SystemsFire     — each major system must produce output across MOST seeds, so a single unlucky
//                       seed won't fail the build, but a genuinely fragile or disabled system will.
public class WsgFuzzTests
{
    private static readonly ulong[] Seeds = { 1, 2, 3, 7, 42, 99, 777, 2024 };
    private const int Years = 250;

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

    private static readonly string Story = File.ReadAllText(FindWsg());

    private static Database Run(ulong seed)
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors.Count(e => e.Severity == StoryParser.Severity.Error), Is.EqualTo(0),
            string.Join("\n", errors));
        db.SetSeed(seed); // before Init so @start events draw from this seed too
        db.History = new();
        db.Init();
        db.Ctx.PassYears(Years, true);
        return db;
    }

    private static void AssertRefsValid(Database db, EntityType type, string propName, ulong seed)
    {
        var prop = type.GetPropertyId(propName);
        foreach (var e in db.Entities)
        {
            if (e.Type != type.Id) continue;
            var r = e.GetProperty(prop);
            if (r.Id.IsNull) continue; // null is fine
            Assert.That(db.TryGetEntity(r.Id, out _), Is.True,
                $"seed {seed}: {type.Name}.{propName} points at a non-existent entity #{r.Id.Id}");
        }
    }

    [TestCaseSource(nameof(Seeds))]
    public void InvariantsHold(ulong seed)
    {
        var db = Run(seed);
        var person = db.GetEntityType("Person");
        var country = db.GetEntityType("Country");
        var alive = person.GetPropertyId("alive");
        var title = person.GetPropertyId("title");

        // 1) No realm is ruled by a corpse: a sitting ruler is a living King, and the ref is valid.
        var rulerProp = country.GetPropertyId("ruler");
        foreach (var c in db.Entities.Where(e => e.Type == country.Id))
        {
            var r = c.GetProperty(rulerProp);
            if (r.Id.IsNull) continue;
            Assert.That(db.TryGetEntity(r.Id, out var king), Is.True, $"seed {seed}: dangling ruler ref");
            Assert.That(king.GetProperty(alive).BoolValue, Is.True, $"seed {seed}: corpse on the throne");
            Assert.That(king.GetProperty(title).IntValue, Is.EqualTo(2), $"seed {seed}: ruler is not a King");
        }

        // 2) Eras form a contiguous timeline with exactly one open (present) age.
        var era = db.GetEntityType("Era");
        var startP = era.GetPropertyId("start_year");
        var endP = era.GetPropertyId("end_year");
        var eras = db.Entities.Where(e => e.Type == era.Id)
            .Select(e => (start: e.GetProperty(startP).IntValue, end: e.GetProperty(endP).IntValue))
            .OrderBy(e => e.start).ToList();
        Assert.That(eras, Is.Not.Empty, $"seed {seed}: no eras");
        Assert.That(eras.Count(e => e.end == 0), Is.EqualTo(1), $"seed {seed}: not exactly one open era");
        for (int i = 0; i < eras.Count - 1; i++)
            Assert.That(eras[i].end, Is.EqualTo(eras[i + 1].start), $"seed {seed}: eras not contiguous");

        // 3) Settlement status is always a valid enum value.
        var settlement = db.GetEntityType("Settlement");
        var status = settlement.GetPropertyId("status");
        foreach (var s in db.Entities.Where(e => e.Type == settlement.Id))
            Assert.That(s.GetProperty(status).IntValue, Is.InRange(0, 3),
                $"seed {seed}: invalid settlement status");

        // 4) Every legend cites a real hero and monster (it's a reference, not flavor text).
        var legend = db.GetEntityType("Legend");
        var heroP = legend.GetPropertyId("hero");
        var monsterP = legend.GetPropertyId("monster");
        foreach (var l in db.Entities.Where(e => e.Type == legend.Id))
        {
            Assert.That(l.GetProperty(heroP).Id.IsNull, Is.False, $"seed {seed}: legend without a hero");
            Assert.That(l.GetProperty(monsterP).Id.IsNull, Is.False, $"seed {seed}: legend without a monster");
        }

        // 5) A slain monster always records its slayer (the only way a monster dies is being slain).
        var monster = db.GetEntityType("Monster");
        var mAlive = monster.GetPropertyId("alive");
        var slainBy = monster.GetPropertyId("slain_by");
        foreach (var m in db.Entities.Where(e => e.Type == monster.Id))
            if (!m.GetProperty(mAlive).BoolValue)
                Assert.That(m.GetProperty(slainBy).Id.IsNull, Is.False, $"seed {seed}: dead monster with no slayer");

        // 6) Key relationship refs never dangle.
        AssertRefsValid(db, person, "parent1", seed);
        AssertRefsValid(db, person, "partner", seed);
        AssertRefsValid(db, country, "ruler", seed);
    }

    [Test]
    public void SystemsFireAcrossSeeds()
    {
        (string label, Func<string, bool> match)[] systems =
        {
            ("coronation", t => t.Contains("is crowned ruler of")),
            ("succession or vacancy", t => t.Contains("succeeds") || t.Contains("is left vacant")),
            ("settlement founded", t => t.Contains("founds the village of")),
            ("settlement grew", t => t.Contains("grows into a")),
            ("war", t => t.Contains("start a war")),
            ("artifact forged", t => t.Contains("forges the")),
            ("legendary wizard", t => t.Contains("attains legendary mastery")),
            ("miracle", t => t.Contains("receives a miracle")),
            ("temple", t => t.Contains("raises a temple")),
            ("faction founded", t => t.Contains("founds the cult") || t.Contains("founds the knightly order")
                                     || t.Contains("founds the thieves guild") || t.Contains("founds the mage circle")
                                     || t.Contains("founds the merchant league")),
            ("faction feud", t => t.Contains("strikes down the outlaw")),
            ("monster awakens", t => t.Contains("awakens in")),
            ("legend born", t => t.Contains("a legend is born")),
            ("age turns", t => t.Contains("begins in the year")),
        };

        var seedCount = new int[systems.Length];
        foreach (var seed in Seeds)
        {
            var db = Run(seed);
            var fired = new bool[systems.Length];
            foreach (var rec in db.Records)
                for (int i = 0; i < systems.Length; i++)
                    if (!fired[i] && systems[i].match(rec.Text))
                        fired[i] = true;
            for (int i = 0; i < systems.Length; i++)
                if (fired[i]) seedCount[i]++;
        }

        // Require a strong majority of seeds (tolerates one unlucky world, fails a fragile system).
        int threshold = (Seeds.Length * 3 + 3) / 4; // ceil(0.75 * n)
        for (int i = 0; i < systems.Length; i++)
            Assert.That(seedCount[i], Is.GreaterThanOrEqualTo(threshold),
                $"system '{systems[i].label}' fired in only {seedCount[i]}/{Seeds.Length} seeds — likely fragile");
    }

    [Test]
    public void SameSeedIsDeterministic()
    {
        var a = Run(42);
        var b = Run(42);
        Assert.That(a.Records.Select(r => r.Text),
            Is.EqualTo(b.Records.Select(r => r.Text)).AsCollection,
            "the same seed must reproduce the same chronicle");
    }

    // The point of per-event RNG streams: adding a rule must not perturb the randomness of others.
    [Test]
    public void AddingAStateFreeEventDoesNotPerturbOthers()
    {
        List<string> Crownings(Database db) => db.Records
            .Where(r => r.Text.Contains("is crowned ruler of"))
            .Select(r => r.Year + " " + StoryPrinter.StripMarkup(r.Text))
            .ToList();

        var baseline = Crownings(Run(42));

        // Same story + a trivial event that draws RNG (its frequency) but mutates no world state.
        // With one shared stream this used to reshuffle every other rule's draws; with per-event
        // streams the crowning chronicle must be byte-identical.
        var modified = Story + "\n@frequency(1, PerXYear, 3)\nevent zzz_probe {\n    record('probe')\n}\n";
        var db = StoryParser.Parse(modified, out var errs);
        Assert.That(errs.Count(e => e.Severity == StoryParser.Severity.Error), Is.EqualTo(0), string.Join("\n", errs));
        db.SetSeed(42);
        db.History = new();
        db.Init();
        db.Ctx.PassYears(Years, true);
        var after = Crownings(db);

        Assert.That(after, Is.EqualTo(baseline).AsCollection,
            "adding a state-free event must leave every other rule's outcomes unchanged");
    }
}
