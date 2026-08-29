namespace TestProject1;

/// Snapshot assertions for the corpus-wide parser tests.
///
/// These replace the differential suites that compared the parser against the frozen ANTLR
/// snapshot. That comparison answered "did the rewrite change anything?", which was the right
/// question during the migration and is now permanently answered; the question worth keeping is
/// "did *this* change alter how the corpus parses or runs?", and a committed snapshot answers it
/// without keeping a second parser (and a JDK) in the build.
///
/// Re-bless with:  UPDATE_GOLDENS=1 dotnet test TestProject1
public static class Golden
{
    public static string RepoRoot { get; } = FindRepoRoot();

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MoiraiCli", "w.sg")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate the repo root above " + AppContext.BaseDirectory);
    }

    /// The sample stories, used as a realistic corpus. MoiraiCli/test.sg is excluded: it contains a
    /// double-quoted string the grammar has never supported.
    public static IEnumerable<string> Corpus()
    {
        yield return "MoiraiCli/w.sg";
        yield return "MoiraiCli/space.sg";
        yield return "MoiraiWebServer/wwwroot/space.sg";
    }

    public static string Read(string relativePath)
    {
        var full = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
            Assert.Inconclusive($"corpus file missing: {full}");
        return File.ReadAllText(full).Replace("\r\n", "\n");
    }

    static string Dir => Path.Combine(RepoRoot, "TestProject1", "Golden");

    static bool UpdateRequested => Environment.GetEnvironmentVariable("UPDATE_GOLDENS") is "1" or "true";

    public static void Verify(string name, string actual)
    {
        actual = actual.Replace("\r\n", "\n");
        var path = Path.Combine(Dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (UpdateRequested || !File.Exists(path))
        {
            File.WriteAllText(path, actual);
            if (!UpdateRequested)
                Assert.Inconclusive($"golden '{name}' did not exist and has been created -- review it, then commit it");
            return;
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        if (expected == actual)
            return;

        Assert.Fail($"golden '{name}' differs.\n{DescribeFirstDivergence(expected, actual)}\n\n" +
                    "If this change is intended, re-bless with: UPDATE_GOLDENS=1 dotnet test TestProject1");
    }

    /// A raw inequality on a thousand-line snapshot is unreadable; point at the first differing line.
    public static string DescribeFirstDivergence(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        int i = 0;
        while (i < e.Length && i < a.Length && e[i] == a[i])
            i++;

        if (i == e.Length && i == a.Length)
            return "(no line-level difference -- trailing newline or line-ending mismatch)";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"first difference at line {i + 1} (expected {e.Length} lines, actual {a.Length} lines)");
        for (int c = Math.Max(0, i - 3); c < Math.Min(Math.Max(e.Length, a.Length), i + 4); c++)
        {
            var marker = c == i ? ">>" : "  ";
            sb.AppendLine($"{marker} {c + 1,5} exp: {(c < e.Length ? e[c] : "<eof>")}");
            sb.AppendLine($"{marker} {c + 1,5} act: {(c < a.Length ? a[c] : "<eof>")}");
        }

        return sb.ToString();
    }
}
