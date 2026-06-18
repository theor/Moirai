using Moirai.Parser;

namespace TestProject1;

public class ProfilerTests
{
    // Deterministic story: `spawn` and `age_up` each run exactly once per year
    // (EveryXYear(1,1)); `on_birth` fires once per spawned Person; `on_death` is
    // evaluated against every change but never matches (nothing ever dies).
    private const string Story = @"
entity Person {
    prop alive: bool
    prop age: number
}
@start
event create_time {
    create Time $t: 'time'
    set $t.year = 0
}
@frequency(1, EveryXYear, 1)
event spawn {
    create Person $p: 'p'
    set $p.alive = true
}
@frequency(1, EveryXYear, 1)
event age_up {
    each Person $p: (alive = true) {
        set $p.age = $p.age + 1
    }
}
trigger on_birth {
    when_created Person
    set $new.age = 0
}
trigger on_death {
    when Person and $new.alive = false
    record('died')
}";

    [Test]
    public void ProfilesEventsAndTriggers()
    {
        const int years = 50;
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));

        db.History = new();
        db.ProfilingEnabled = true;
        db.Init();                       // runs @start create_time (before profiling begins)
        db.Ctx.PassYears(years, true);

        var prof = db.ExecProfiler;
        Assert.That(prof, Is.Not.Null, "PassYears should allocate a profiler when enabled");
        Console.WriteLine(prof!.Report());

        Assert.That(prof.Years, Is.EqualTo(years));

        // Events: deterministic schedule -> exactly one run per year, all succeed.
        var spawn = prof.Events.Single(e => e.Name == "spawn");
        var ageUp = prof.Events.Single(e => e.Name == "age_up");
        Assert.That(spawn.Attempts, Is.EqualTo(years));
        Assert.That(spawn.Successes, Is.EqualTo(years));
        Assert.That(ageUp.Attempts, Is.EqualTo(years));

        // Triggers: on_birth matches every spawned Person; on_death never matches.
        var onBirth = prof.Triggers.Single(t => t.Name == "on_birth");
        var onDeath = prof.Triggers.Single(t => t.Name == "on_death");
        Assert.That(onBirth.Successes, Is.EqualTo(years));
        Assert.That(onBirth.HitRate, Is.EqualTo(1.0));
        Assert.That(onDeath.Attempts, Is.GreaterThan(0));
        Assert.That(onDeath.Successes, Is.Zero);
        Assert.That(onDeath.HitRate, Is.Zero);

        // Self time is well formed: never negative, never exceeds inclusive, and the sum
        // of all self time is accounted for within the total run wall time.
        foreach (var s in prof.Events.Concat(prof.Triggers))
        {
            Assert.That(s.SelfTicks, Is.GreaterThanOrEqualTo(0), s.Name);
            Assert.That(s.SelfTicks, Is.LessThanOrEqualTo(s.InclusiveTicks), s.Name);
        }
        double selfMs = prof.Events.Concat(prof.Triggers).Sum(s => s.SelfMs);
        Assert.That(selfMs, Is.LessThanOrEqualTo(prof.ElapsedMs + 1));
    }

    // Baseline profile of the canonical large story (w.sg) over a realistic horizon.
    // Not an assertion test — it parses w.sg, simulates `years`, and prints the profiler report.
    // Each horizon is run REPEATS times on a fresh DB and we report the MIN PassYears time: wall-clock
    // here is dominated by host contention (observed ~2-3x variance across runs), so min is the most
    // stable proxy for actual compute and the only number safe to A/B optimizations against.
    private const int Repeats = 5;

    [TestCase(50)]
    [TestCase(200)]
    [TestCase(500)]
    [TestCase(1000)]
    public void ProfileWsg(int years)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "MoiraiCli", "w.sg");
        var text = File.ReadAllText(path);

        double best = double.MaxValue;
        ExecutionProfiler? bestProf = null;
        int entities = 0, records = 0;
        for (int rep = 0; rep < Repeats; rep++)
        {
            var db = StoryParser.Parse(text, out var errors);
            Assert.That(errors, Is.Empty, $"{errors.Count} parse errors:\n" + string.Join("\n", errors));

            db.History = new();
            db.ProfilingEnabled = true;
            db.Init();
            db.Ctx.PassYears(years, true);

            var prof = db.ExecProfiler!;
            if (prof.ElapsedMs < best)
            {
                best = prof.ElapsedMs;
                bestProf = prof;
                entities = db.Entities.Count();
                records = db.Records.Count;
            }
        }

        Assert.That(bestProf, Is.Not.Null);
        Console.WriteLine($"=== BEST of {Repeats}: {years} years in {best:F1} ms ===");
        Console.WriteLine(bestProf!.Report());
        Console.WriteLine($"entities: {entities}, records: {records}");
    }

    [Test]
    public void NotAllocatedWhenDisabled()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));

        db.History = new();
        db.Init();
        db.Ctx.PassYears(5, true);

        Assert.That(db.ExecProfiler, Is.Null, "profiler must not be allocated unless ProfilingEnabled is set");
    }
}
