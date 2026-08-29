using Moirai.Parser;

namespace TestProject1;

// The engine's always-on rule counters (EventTrigger.Attempts/Successes), which back the web UI's
// Rules page. They exist so a rule that never fires — a silent story bug the records feed cannot show —
// is visible without running under --profile.
public class RuleCoverageTests
{
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

    private static Database Load(ulong seed, bool profile)
    {
        var db = StoryParser.Parse(File.ReadAllText(FindWsg()), out var errors);
        Assert.That(errors.Count(e => e.Severity == StoryParser.Severity.Error), Is.EqualTo(0),
            string.Join("\n", errors));
        db.SetSeed(seed);
        db.History = new();
        db.ProfilingEnabled = profile;
        db.Init();
        return db;
    }

    private static IEnumerable<EventTrigger> AllRules(Database db) => db.Actions.Concat(db.Triggers);

    [Test]
    public void SuccessesNeverExceedAttempts()
    {
        var db = Load(42, false);
        db.Ctx.PassYears(Years, true);
        foreach (var r in AllRules(db))
            Assert.That(r.Successes, Is.LessThanOrEqualTo(r.Attempts), $"{r.Name}");
    }

    // The counters and the profiler measure the same thing from a fresh world, so they must agree.
    // If they drift, one of the two recording sites was moved without the other.
    [Test]
    public void CountersAgreeWithTheProfilerOverOneRun()
    {
        var db = Load(42, true);
        db.Ctx.PassYears(Years, true);
        var profiler = db.ExecProfiler;
        Assert.That(profiler, Is.Not.Null);

        foreach (var (stats, rules, kind) in new[]
                 {
                     (profiler!.Events, db.Actions, "event"),
                     (profiler.Triggers, db.Triggers, "trigger"),
                 })
        {
            var byId = stats.ToDictionary(s => s.Id);
            foreach (var rule in rules)
            {
                // Init() runs @start events before PassYears allocates the profiler, so those events
                // legitimately carry one uncounted attempt. Every other rule must match exactly.
                if (rule.Filter is FilterAtStart)
                    continue;
                long attempts = byId.TryGetValue(rule.Id, out var s) ? s.Attempts : 0;
                long ok = s?.Successes ?? 0;
                Assert.That(rule.Attempts, Is.EqualTo(attempts), $"{kind} {rule.Name} attempts");
                Assert.That(rule.Successes, Is.EqualTo(ok), $"{kind} {rule.Name} successes");
            }
        }
    }

    // The profiler is per-run; these counters are per-world. Two passes must add up.
    [Test]
    public void CountersAccumulateAcrossRuns()
    {
        var db = Load(42, false);
        db.Ctx.PassYears(100, true);
        var afterFirst = AllRules(db).ToDictionary(r => (r.IsTrigger, r.Id), r => r.Attempts);
        db.Ctx.PassYears(100, true);

        Assert.That(AllRules(db).Sum(r => r.Attempts),
            Is.GreaterThan(afterFirst.Values.Sum()), "a second pass must add attempts");
        foreach (var r in AllRules(db))
            Assert.That(r.Attempts, Is.GreaterThanOrEqualTo(afterFirst[(r.IsTrigger, r.Id)]),
                $"{r.Name} attempts went backwards");
    }

    // A rule that never fires across a long run on several seeds is dead code in the story. This is the
    // signal the Rules page surfaces; asserting it here keeps w.sg honest as the canonical example.
    [Test]
    public void WsgHasNoPermanentlyDeadRules()
    {
        ulong[] seeds = { 1, 42, 777 };
        var everFired = new HashSet<string>();
        var allRules = new List<string>();
        foreach (var seed in seeds)
        {
            var db = Load(seed, false);
            db.Ctx.PassYears(Years, true);
            allRules = AllRules(db).Select(r => (r.IsTrigger ? "trigger " : "event ") + r.Name).ToList();
            foreach (var r in AllRules(db))
                if (r.Successes > 0)
                    everFired.Add((r.IsTrigger ? "trigger " : "event ") + r.Name);
        }

        var dead = allRules.Where(n => !everFired.Contains(n)).ToList();
        Assert.That(dead, Is.Empty,
            $"these rules never ran to completion on any of {seeds.Length} seeds over {Years} years:\n  "
            + string.Join("\n  ", dead));
    }
}
