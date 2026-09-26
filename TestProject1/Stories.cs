namespace TestProject1;

/// Where the tests' stories come from.
///
/// Two kinds, kept apart on purpose. <see cref="Wsg"/> is the canonical sample story: tests that are
/// *about* it (it parses, it runs clean across seeds, it has no dead rules, its goldens, its benchmarks)
/// read it here. A test of the engine or the viewer API should not -- w.sg is edited as a story, and a
/// test that leans on its details (its start year, its ages, who marries by year 120) breaks whenever
/// it is. Those tests write their story inline, or load a small fixture from TestProject1/Stories/
/// with <see cref="Load"/>, where the story is written to say exactly what the tests rely on.
public static class Stories
{
    public static string WsgPath { get; } = Path.Combine(Golden.RepoRoot, "MoiraiCli", "w.sg");

    /// The text of MoiraiCli/w.sg, read once.
    public static string Wsg => _wsg ??= File.ReadAllText(WsgPath);
    private static string? _wsg;

    /// A fixture story from TestProject1/Stories/, e.g. <c>Load("village.sg")</c>.
    public static string Load(string name) =>
        File.ReadAllText(Path.Combine(Golden.RepoRoot, "TestProject1", "Stories", name));
}
