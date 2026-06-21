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
