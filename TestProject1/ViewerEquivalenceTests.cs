using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moirai.Api;

namespace TestProject1;

/// <summary>
/// What a viewer is shown of a world, hashed: the overview and every chartable series, the whole changeset
/// log as the Changesets page lists it, entities' past states, biographies and change lists, the chronicle, the
/// notable entities, and some family trees -- all serialized the way they go over the wire. Explicit: it exists to compare
/// two builds of the engine (run it on each, in Release, and compare the hashes), which is how the
/// changeset log's move from entity copies to deltas was shown to change nothing a page can see.
/// </summary>
[Explicit("comparison between builds: run by hand, in Release")]
public class ViewerEquivalenceTests
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

    [TestCase(42ul, 400)]
    [TestCase(7ul, 400)]
    [TestCase(1234ul, 400)]
    [TestCase(42ul, 1000)]
    [TestCase(7ul, 1000)]
    [TestCase(1234ul, 1000)]
    public void WhatTheViewerSees(ulong seed, int years)
    {
        var s = new WorldSession(File.ReadAllText(FindWsg()), seed);
        s.PassYears(years);
        var db = s.Database;
        long start = db.StartYear, end = db.Ctx.Year;
        int entities = db.Entities.Count();

        var parts = new List<(string Name, object Value)>();
        var overview = s.GetWorldOverview();
        parts.Add(("overview", overview));
        foreach (var p in overview.Properties)
            parts.Add(($"series {p.TypeName}.{p.PropertyName}", s.GetPropertySeries(p.TypeId, p.PropertyName)));
        parts.Add(("changesets", s.GetChangesets(0, s.GetChangesetsCount())));
        parts.Add(("chronicle", s.GetChronicle(12)));
        parts.Add(("notable", s.GetNotable(5)));

        var years4 = new[] { start + 1, start + (end - start) / 3, start + 2 * (end - start) / 3, end - 1 };
        for (uint eid = 1; eid <= entities; eid += 7)
            foreach (var y in years4)
                parts.Add(($"at {eid} {y}", s.GetEntityAt(eid, y)));
        for (uint eid = 1; eid <= entities; eid += 25)
        {
            parts.Add(($"bio {eid}", s.GetBiography(eid)));
            parts.Add(($"changes {eid}", s.GetEntityChangesets(eid)));
        }

        for (uint eid = 1; eid <= entities; eid += 150)
            parts.Add(($"family {eid}", s.GetFamilyTree(eid, 3)));

        var all = new StringBuilder();
        var perPart = new Dictionary<string, StringBuilder>();
        foreach (var (name, value) in parts)
        {
            var json = JsonSerializer.Serialize(value, value.GetType(), MoiraiWireJson.Options);
            all.Append(name).Append('=').Append(json).Append('\n');
            // Grouped by kind, so a mismatch says which reader drifted.
            var kind = name.Split(' ')[0];
            if (!perPart.TryGetValue(kind, out var sb))
                perPart[kind] = sb = new StringBuilder();
            sb.Append(json).Append('\n');
        }

        TestContext.Out.WriteLine($"seed {seed} {years}y: ALL {Hash(all)}  " +
                                  string.Join("  ", perPart.OrderBy(k => k.Key).Select(k => $"{k.Key} {Hash(k.Value)}")));
    }

    private static string Hash(StringBuilder sb) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..10];
}
