using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Moirai.LanguageServer.Tests;

/// Locates the .sg sample stories used as a realistic corpus for the golden tests. The repo has no
/// dedicated test-data directory -- the corpus is the app's own sample stories, so we walk up from
/// the test binary to the repo root rather than relying on a "../../../.." relative path (the
/// approach TestProject1's differential suites use, which breaks if the output path ever changes).
public static class Corpus
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

        throw new FileNotFoundException("Could not locate the repo root (looked for MoiraiCli/w.sg above " +
                                        AppContext.BaseDirectory + ")");
    }

    /// Relative paths, deliberately also used as the golden-file stem so a golden is traceable to
    /// its source. MoiraiCli/test.sg is excluded for the same reason TokenizerDifferentialTests
    /// excludes it: it contains a double-quoted string the grammar has never supported.
    public static IEnumerable<string> Files()
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
}

/// Snapshot ("golden file") assertions. These exist because the two riskiest areas of the
/// ANTLR->Superpower LSP migration -- document formatting and semantic highlighting -- had no test
/// asserting their actual output: the formatter was only checked for "produced more than zero
/// edits". A golden is a change detector, not a specification: when a diff shows up, read it and
/// either fix the regression or re-bless the file deliberately.
///
/// Re-bless everything with:  UPDATE_GOLDENS=1 dotnet test Moirai.LanguageServer.Tests
public static class Golden
{
    static string Dir => Path.Combine(Corpus.RepoRoot, "Moirai.LanguageServer.Tests", "Golden");

    static bool UpdateRequested =>
        Environment.GetEnvironmentVariable("UPDATE_GOLDENS") is "1" or "true";

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
                    "If this change is intended, re-bless with: UPDATE_GOLDENS=1 dotnet test Moirai.LanguageServer.Tests");
    }

    /// A raw "expected != actual" on a 1000-line snapshot is unreadable. Point at the first line
    /// that differs and show a window around it -- same idea as
    /// TokenizerDifferentialTests.DescribeFirstDivergence.
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
            sb.AppendLine($"{marker} {c + 1,5} exp: {(c < e.Length ? Quote(e[c]) : "<eof>")}");
            sb.AppendLine($"{marker} {c + 1,5} act: {(c < a.Length ? Quote(a[c]) : "<eof>")}");
        }

        return sb.ToString();
    }

    // Whitespace is the whole point of a formatting golden, so make it visible.
    static string Quote(string s) => "'" + s.Replace(" ", "·") + "'";
}

public static class TextEdits
{
    /// Applies LSP TextEdits the way a client does: edits are non-overlapping and applied as if
    /// simultaneously, so we walk the (start-ascending) list backwards -- later edits first, which
    /// keeps every earlier edit's offsets valid. Ties at the same start position stay in array
    /// order because reversing the iteration reverses them back.
    public static string Apply(string text, IReadOnlyList<TextEdit> edits)
    {
        var sorted = edits.OrderBy(e => e.Range.Start.Line).ThenBy(e => e.Range.Start.Character).ToList();
        var sb = new System.Text.StringBuilder(text);
        var lineStarts = LineStarts(text);

        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var edit = sorted[i];
            int start = Offset(lineStarts, text.Length, edit.Range.Start);
            int end = Offset(lineStarts, text.Length, edit.Range.End);
            if (end < start)
                (start, end) = (end, start);
            sb.Remove(start, end - start);
            sb.Insert(start, edit.NewText);
        }

        return sb.ToString();
    }

    static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                starts.Add(i + 1);
        return starts.ToArray();
    }

    static int Offset(int[] lineStarts, int length, Position pos)
    {
        if (pos.Line >= lineStarts.Length)
            return length;
        return Math.Min(length, lineStarts[pos.Line] + pos.Character);
    }
}
