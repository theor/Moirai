using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Moirai.Parser;

namespace TestProject1;

/// <summary>
/// What a pass of w.sg costs the garbage collector: bytes allocated, collections per generation, time
/// paused, and the heap still live afterwards. Explicit, so CI never runs it -- the numbers are only
/// worth anything on a quiet machine in Release:
///
///   dotnet test TestProject1 -c Release --filter "FullyQualifiedName~AllocationBenchmarkTests"
///
/// Each row carries a fingerprint of everything the pass produced. An allocation change must leave it
/// exactly where it was: the point is to make the same world with less garbage.
/// </summary>
[Explicit("benchmark: run by hand, in Release")]
public class AllocationBenchmarkTests
{
    private static readonly ulong[] Seeds = { 42, 7, 1234 };
    private const int Runs = 3;

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

    private readonly record struct Sample(
        double Ms, long Bytes, int Gen0, int Gen1, int Gen2, double PauseMs, long Retained, string Fingerprint);

    private static Sample Run(string story, ulong seed, int years, bool history = true)
    {
        var db = StoryParser.Parse(story, out _);
        db.SetSeed(seed);
        db.History = history ? new() : null;
        db.Init();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long bytes = GC.GetTotalAllocatedBytes(true);
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var pause = GC.GetTotalPauseDuration();
        var sw = Stopwatch.StartNew();

        db.Ctx.PassYears(years, true);

        sw.Stop();
        var sample = new Sample(
            sw.Elapsed.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(true) - bytes,
            GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2,
            (GC.GetTotalPauseDuration() - pause).TotalMilliseconds,
            GC.GetTotalMemory(true),
            Fingerprint(db));
        GC.KeepAlive(db);
        return sample;
    }

    /// <summary>
    /// Everything a pass produced that a viewer can see, hashed: the records, then the history, apart, so
    /// a change that only touches the history shows that the story itself did not move.
    /// </summary>
    public static string Fingerprint(Database db)
    {
        var sb = new StringBuilder();
        foreach (var r in db.Records)
            sb.Append(r.Year).Append('|').Append(r.ChangesetId).Append('|').Append(r.Firing).Append('|')
                .Append(r.Rule).Append('|').Append(r.Weight).Append('|')
                .Append(string.Join(",", r.Participants.Select(p => p.Id))).Append('|')
                .Append(string.Join(",", r.Tags ?? Array.Empty<string>())).Append('|')
                .Append(r.Text).Append('\n');
        var records = Hash(sb);
        sb.Clear();
        foreach (var cs in db.History?.Changesets ?? [])
        {
            sb.Append(cs.Id).Append(' ').Append(cs.ActionName).Append(' ').Append(cs.Year).Append(' ')
                .Append(cs.Firing).Append(':');
            foreach (var ch in cs.Changes)
            {
                sb.Append(ch.Prev.Id.Id).Append('/').Append(ch.New.Id.Id).Append('[');
                AppendSet(sb, ch.New);
                sb.Append("][");
                if (!ch.Prev.Id.IsNull)
                    AppendSet(sb, ch.Prev);
                sb.Append(']');
            }

            sb.Append('\n');
        }

        if (db.History == null)
            sb.Append("no history");
        return $"R{records} H{Hash(sb)} r{db.Records.Count} e{db.Entities.Count()} c{db.History?.Changesets.Count} y{db.Ctx.Year}";
    }

    private static string Hash(StringBuilder sb) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..10];

    // Only the properties that are set, in id order: what an entity says, not how it is laid out, so a
    // change of storage cannot move the hash on its own.
    private static void AppendSet(StringBuilder sb, Entity e)
    {
        foreach (var p in e.Properties.Where(p => p.Id.IsValid).OrderBy(p => p.Id.Id))
            sb.Append(p.Id.Id).Append('=').Append(p.Value.Value).Append(p.Value.IntValue).Append(',');
    }

    [TestCase(400)]
    [TestCase(1000)]
    public void PassOfWsg(int years) => Bench(years, true);

    /// <summary>The same passes with no history kept: roughly the ceiling of what cheaper changesets can save.</summary>
    [TestCase(1000)]
    public void PassOfWsgWithoutHistory(int years) => Bench(years, false);

    private static void Bench(int years, bool history)
    {
        var story = File.ReadAllText(FindWsg());
        // One throwaway pass so the JIT is not what gets measured.
        Run(story, Seeds[0], Math.Min(years, 100), history);

        TestContext.Out.WriteLine(
            $"{"seed",6} {"ms",8} {"alloc MB",9} {"gen0",5} {"gen1",5} {"gen2",5} {"pause ms",9} {"live MB",8}  fingerprint");
        foreach (var seed in Seeds)
        {
            var samples = Enumerable.Range(0, Runs).Select(_ => Run(story, seed, years, history)).ToList();
            Assert.That(samples.Select(s => s.Fingerprint).Distinct().Count(), Is.EqualTo(1),
                "the same seed made two different worlds");
            var best = samples.MinBy(s => s.Ms);
            TestContext.Out.WriteLine(
                $"{seed,6} {best.Ms,8:F0} {best.Bytes / 1048576.0,9:F1} {best.Gen0,5} {best.Gen1,5} {best.Gen2,5} " +
                $"{best.PauseMs,9:F1} {best.Retained / 1048576.0,8:F1}  {best.Fingerprint}");
        }
    }

    /// <summary>The profiler's per-rule table, allocation column included: which rules make the garbage.</summary>
    [Test]
    public void ProfileOfWsg()
    {
        var db = StoryParser.Parse(File.ReadAllText(FindWsg()), out _);
        db.SetSeed(Seeds[0]);
        db.History = new();
        db.Init();
        db.ProfilingEnabled = true;
        db.Ctx.PassYears(1000, true);
        TestContext.Out.WriteLine(db.ExecProfiler!.Report());
    }

    /// <summary>
    /// What the allocations of a 1000-year pass are, by type: the runtime's allocation-tick events, one
    /// sample per ~100 KB allocated, each naming the type that crossed the threshold.
    /// </summary>
    [Test]
    public void AllocationsByType()
    {
        var story = File.ReadAllText(FindWsg());
        Run(story, Seeds[0], 100); // warm up
        var db = StoryParser.Parse(story, out _);
        db.SetSeed(Seeds[0]);
        db.History = new();
        db.Init();
        using var listener = new AllocationListener(OperatingSystem.IsWindows() ? GetCurrentThreadId() : 0);
        db.Ctx.PassYears(1000, true);
        listener.Dispose();
        // A closure's type name says nothing about where it is: name the classes that declare one.
        var generated = typeof(Database).Assembly.GetTypes().Concat(typeof(Moirai.Api.WorldSession).Assembly.GetTypes())
            .Where(t => t.Name.StartsWith("<>c__DisplayClass"))
            .ToLookup(t => t.Name, t => t.DeclaringType?.FullName);
        foreach (var (type, bytes) in listener.ByType.OrderByDescending(kv => kv.Value).Take(30))
        {
            var owners = generated[type].ToList();
            TestContext.Out.WriteLine($"{bytes / 1048576.0,8:F2} MB  {type}" +
                                      (owners.Count > 0 ? $"  (in {string.Join(" or ", owners)})" : ""));
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // Only the simulating thread's samples (when its OS id is known): the test host allocates on its own.
    private sealed class AllocationListener(long thread) : System.Diagnostics.Tracing.EventListener
    {
        public readonly Dictionary<string, long> ByType = new();
        private bool _on = true;

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (!_on || e.EventName == null || !e.EventName.StartsWith("GCAllocationTick") || e.Payload == null)
                return;
            if (thread != 0 && e.OSThreadId != thread)
                return;
            var names = e.PayloadNames!;
            var type = (string?)e.Payload[names.IndexOf("TypeName")] ?? "?";
            var amount = Convert.ToInt64(e.Payload[names.IndexOf("AllocationAmount64")]);
            lock (ByType)
                ByType[type] = ByType.GetValueOrDefault(type) + amount;
        }

        public override void Dispose()
        {
            _on = false;
            base.Dispose();
        }
    }
}
