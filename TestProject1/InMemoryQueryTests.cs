using Moirai.Parser;

namespace TestProject1;

// Validates the in-memory query backend (the SQLite replacement spike) against the SQLite backend.
// See plan: could-sqlite-be-replaced.
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

    // Differential test: run both backends over the same RNG draws for every pick/each and assert they
    // return the same entity / same result set (and leave the RNG in the same state). Any divergence
    // throws inside PickRandom/FindAll, failing the test with the offending predicate.
    [Test]
    public void InMemoryMatchesSqliteOnWsg()
    {
        Database.VerifyInMemoryQuery = true;
        try
        {
            var records = RunWsg(300);
            Assert.That(records.Count, Is.GreaterThan(0), "a 300-year run should produce records");
        }
        finally
        {
            Database.VerifyInMemoryQuery = false;
        }
    }

    // Quick wall-clock comparison of the two backends. Explicit (run on demand, ideally in Release):
    // dotnet test -c Release --filter "Name~CompareBackendSpeed"
    [Test]
    [Explicit]
    public void CompareBackendSpeed()
    {
        var story = File.ReadAllText(FindWsg());
        foreach (var years in new[] { 200, 500, 1000 })
        {
            double sql = TimeRun(story, years, false);
            double mem = TimeRun(story, years, true);
            TestContext.Progress.WriteLine(
                $"{years,5} yr:  sqlite {sql,8:F1} ms   in-memory {mem,8:F1} ms   speedup {sql / mem,5:F2}x");
        }
    }

    private static double TimeRun(string story, int years, bool inMemory)
    {
        // Warm once, then take the best of 3 to damp JIT/GC noise.
        double best = double.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            Database.UseInMemoryQuery = inMemory;
            var db = StoryParser.Parse(story, out _);
            db.History = new();
            db.Init();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            db.Ctx.PassYears(years, true);
            sw.Stop();
            Database.UseInMemoryQuery = false;
            if (i > 0) best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    // Exact-output requirement: a full run with the in-memory backend as the live path must produce a
    // byte-identical narrative record stream to the SQLite backend, for the same seed.
    [Test]
    public void InMemoryProducesIdenticalRecordsToSqlite()
    {
        var sqlite = RunWsg(250);

        Database.UseInMemoryQuery = true;
        List<string> inMemory;
        try
        {
            inMemory = RunWsg(250);
        }
        finally
        {
            Database.UseInMemoryQuery = false;
        }

        Assert.That(inMemory.Count, Is.EqualTo(sqlite.Count), "record counts must match");
        for (int i = 0; i < sqlite.Count; i++)
            Assert.That(inMemory[i], Is.EqualTo(sqlite[i]), $"record #{i} diverged");
    }
}
