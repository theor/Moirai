using BenchmarkDotNet.Attributes;
using Moirai.Parser;

namespace Moirai.Benchmarks;

/// <summary>
/// End-to-end simulation benchmark over the canonical large story (MoiraiCli/w.sg):
/// parse -> Init -> PassYears(N). Each iteration runs on a fresh <see cref="Database"/> because
/// PassYears advances world state and cannot be replayed. Parsing w.sg is sub-millisecond and is
/// dwarfed by the simulation at every horizon, so it is left inside the measured region to reflect
/// the real "run this story for N years" cost.
/// </summary>
[MemoryDiagnoser]
public class WsgBenchmark
{
    [Params(50, 200, 500, 1000)]
    public int Years;

    private string _story = "";

    [GlobalSetup]
    public void Setup()
    {
        _story = File.ReadAllText(BenchmarkPaths.FindStory("w.sg"));
        // Fail fast if w.sg no longer parses (e.g. grammar drift) rather than benchmarking nothing.
        StoryParser.Parse(_story, out var errors);
        if (errors.Count > 0)
            throw new InvalidOperationException($"w.sg has {errors.Count} parse errors:\n" + string.Join("\n", errors));
    }

    [Benchmark]
    public int Simulate()
    {
        var db = StoryParser.Parse(_story, out _);
        db.History = new();
        db.Init();
        db.Ctx.PassYears(Years, true);
        return db.Records.Count;
    }
}
