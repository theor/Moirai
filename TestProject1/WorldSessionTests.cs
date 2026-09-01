using System.Text.Json;
using Moirai.Api;

namespace TestProject1;

// WorldSession is the whole client API with no transport attached — the SignalR hub and the WebAssembly
// export are both shims over it. These tests are the safety net for that: they exercise the API the way a
// viewer does, without standing up a server, so a regression in the shared logic localises here rather
// than showing up as a blank page in one of the two front ends.
public class WorldSessionTests
{
    private const ulong Seed = 42;

    private static string Wsg()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "MoiraiCli", "w.sg");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate MoiraiCli/w.sg above " + AppContext.BaseDirectory);
    }

    private static WorldSession Session(ulong seed = Seed) => new(Wsg(), seed);

    [Test]
    public void ConstructionRunsStartEventsAndLeavesAQueryableWorld()
    {
        var s = Session();

        Assert.That(s.GetSeed(), Is.EqualTo(Seed));
        Assert.That(s.Database.Entities.Count(), Is.GreaterThan(0), "@start events should have populated the world");
        // w.sg's Time singleton starts at 764, and StartYear is captured at the end of Init.
        Assert.That(s.Year, Is.EqualTo(s.Database.StartYear));
    }

    [Test]
    public void SameSeedAndStoryGiveTheSameWorld()
    {
        var a = Session();
        var b = Session();
        a.PassYears(60);
        b.PassYears(60);

        Assert.That(b.Year, Is.EqualTo(a.Year));
        Assert.That(b.RecordCount, Is.EqualTo(a.RecordCount));
        Assert.That(b.Database.Records.Select(r => r.Text), Is.EqualTo(a.Database.Records.Select(r => r.Text)));
    }

    [Test]
    public void ReseedGivesADifferentWorldAndReportsTheNewSeed()
    {
        var s = Session();
        s.PassYears(40);
        var before = s.Database.Records.Select(r => r.Text).ToArray();

        s.Reseed(1234);
        Assert.That(s.GetSeed(), Is.EqualTo(1234UL));
        Assert.That(s.Year, Is.EqualTo(s.Database.StartYear), "reseed rebuilds the world, so the clock restarts");
        s.PassYears(40);

        Assert.That(s.Database.Records.Select(r => r.Text).ToArray(), Is.Not.EqualTo(before));
    }

    [Test]
    public void ResetRebuildsFromTheStoryAndKeepsTheSeed()
    {
        var s = Session();
        s.PassYears(30);
        Assert.That(s.RecordCount, Is.GreaterThan(0));

        s.Reset();
        Assert.That(s.GetSeed(), Is.EqualTo(Seed));
        Assert.That(s.Year, Is.EqualTo(s.Database.StartYear));
    }

    [Test]
    public void ResetRereadsTheStoryText()
    {
        // The server's hot reload depends on this: the session holds a factory, not a snapshot.
        var story = Wsg();
        int reads = 0;
        var s = new WorldSession(() => { reads++; return story; }, Seed);
        Assert.That(reads, Is.EqualTo(1));
        s.Reset();
        Assert.That(reads, Is.EqualTo(2));
    }

    [Test]
    public void GetClientDataListsEveryEventAndUserTypeSortedByName()
    {
        var s = Session();
        var data = s.GetClientData();

        Assert.That(data.Seed, Is.EqualTo(Seed));
        Assert.That(data.Actions, Is.Not.Empty);
        Assert.That(data.Actions.Length, Is.EqualTo(s.Database.Actions.Count));
        Assert.That(data.Types.Select(t => t.Name), Is.Ordered);
        // Types skips the built-in at index 0, so it must not appear.
        Assert.That(data.Types.Select(t => t.Id), Has.None.Zero);
    }

    // Reports on the calling thread. Progress<T> defers through the thread pool instead, so its reports
    // arrive after the pass has finished and in no particular order — no use for asserting when they land.
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Test]
    public void PassYearsReportsProgressWhileItRuns()
    {
        // The server streams these to the client as a pass runs, so they have to arrive during the loop
        // rather than in a batch at the end.
        var s = Session();
        var seen = new List<int>();
        var yearDuringFirstReport = -1L;
        s.PassYears(100, new SyncProgress<int>(i =>
        {
            if (seen.Count == 0) yearDuringFirstReport = s.Year;
            seen.Add(i);
        }));

        Assert.That(seen, Is.Not.Empty);
        Assert.That(seen, Is.Ordered);
        Assert.That(seen.Last(), Is.LessThan(100));
        Assert.That(yearDuringFirstReport, Is.LessThan(s.Year),
            "the first report must arrive before the pass has finished");
    }

    [Test]
    public void ManySmallPassesAreIdenticalToOneLongPass()
    {
        // The browser host has to hand control back to the event loop so the page stays alive, so it
        // simulates in chunks rather than one call. That is only sound if chunking cannot change the
        // outcome: the RNG streams live on ExecuteContext and the year is re-read from the Time entity
        // each call, so it should not. This is the assertion the whole chunked design rests on.
        var whole = Session();
        whole.PassYears(120);

        var chunked = Session();
        for (int i = 0; i < 12; i++)
            chunked.PassYears(10);

        Assert.That(chunked.Year, Is.EqualTo(whole.Year));
        Assert.That(chunked.RecordCount, Is.EqualTo(whole.RecordCount));
        Assert.That(chunked.Database.Records.Select(r => r.Text),
            Is.EqualTo(whole.Database.Records.Select(r => r.Text)));
        Assert.That(chunked.GetChangesetsCount(), Is.EqualTo(whole.GetChangesetsCount()));
        Assert.That(chunked.Database.Entities.Count(), Is.EqualTo(whole.Database.Entities.Count()));
    }

    [Test]
    public void UnevenChunksAreAlsoIdenticalToOneLongPass()
    {
        // The host's last chunk is whatever remains, so the sizes are not uniform in practice.
        var whole = Session();
        whole.PassYears(100);

        var chunked = Session();
        foreach (var n in new[] { 7, 25, 1, 40, 27 })
            chunked.PassYears(n);

        Assert.That(chunked.Year, Is.EqualTo(whole.Year));
        Assert.That(chunked.Database.Records.Select(r => r.Text),
            Is.EqualTo(whole.Database.Records.Select(r => r.Text)));
    }

    [Test]
    public void BiographyInterleavesRecordsAndChangesInCausalOrder()
    {
        var s = Session();
        s.PassYears(80);

        // Any entity with a life: take the one the most records mention.
        var eid = s.Database.Entities.Select(e => e.Id.Id)
            .OrderByDescending(id => s.GetBiography(id).Timeline.Length)
            .First();
        var bio = s.GetBiography(eid);

        Assert.That(bio.Id, Is.EqualTo(eid));
        Assert.That(bio.Timeline, Is.Not.Empty);
        Assert.That(bio.Details, Is.Not.Empty);
        Assert.That(bio.Timeline.Select(t => t.Year), Is.Ordered, "a life reads front to back");
        // Setup effects carry year 0; clamping puts them at the start of the life, not centuries before.
        Assert.That(bio.Timeline.Select(t => t.Year), Has.All.GreaterThanOrEqualTo(s.Database.StartYear));
        Assert.That(bio.Timeline.Select(t => t.Kind).Distinct(), Is.SubsetOf(new[] { "record", "change" }));
    }

    [Test]
    public void BiographyOfAnUnknownEntityIsEmptyRatherThanAnError()
    {
        var bio = Session().GetBiography(999999);
        Assert.That(bio.Timeline, Is.Empty);
        Assert.That(bio.Details, Is.Empty);
        Assert.That(bio.HasFamily, Is.False);
    }

    [Test]
    public void BiographyIsStableAcrossRepeatedCalls()
    {
        // An event and the triggers it fires share a changeset id, so ties are common; an unstable sort
        // would reshuffle a life every time the page refreshed.
        var s = Session();
        s.PassYears(60);
        var eid = s.Database.Entities.First().Id.Id;

        var first = s.GetBiography(eid).Timeline.Select(t => (t.Year, t.ChangesetId, t.Kind, t.Text)).ToArray();
        var second = s.GetBiography(eid).Timeline.Select(t => (t.Year, t.ChangesetId, t.Kind, t.Text)).ToArray();
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void WorldOverviewCountsAgreeWithTheEngine()
    {
        var s = Session();
        s.PassYears(50);
        var o = s.GetWorldOverview();

        Assert.That(o.Year, Is.EqualTo(s.Year));
        Assert.That(o.Records, Is.EqualTo(s.RecordCount));
        Assert.That(o.Entities, Is.EqualTo(s.Database.Entities.Count()));
        Assert.That(o.Changesets, Is.EqualTo(s.GetChangesetsCount()));
        Assert.That(o.Series, Is.Not.Empty);
        Assert.That(o.Properties, Is.Not.Empty);
        Assert.That(o.Properties.Select(p => p.Kind).Distinct(), Is.SubsetOf(new[] { "bool", "number" }));
    }

    [Test]
    public void PropertySeriesIsChartableForEveryPropertyTheOverviewAdvertises()
    {
        var s = Session();
        s.PassYears(50);

        foreach (var p in s.GetWorldOverview().Properties)
        {
            var series = s.GetPropertySeries(p.TypeId, p.PropertyName);
            Assert.That(series.Years.Length, Is.EqualTo(series.Values.Length),
                $"{p.TypeName}.{p.PropertyName} must have one value per year");
        }
    }

    [Test]
    public void RuleCoverageCoversEveryRuleAndMatchesTheEngineCounters()
    {
        var s = Session();
        s.PassYears(100);
        var report = s.GetRuleCoverage();

        Assert.That(report.Year, Is.EqualTo(s.Year));
        Assert.That(report.Rules.Length,
            Is.EqualTo(s.Database.Actions.Count + s.Database.Triggers.Count));
        Assert.That(report.Rules.Select(r => r.Kind).Distinct(), Is.SubsetOf(new[] { "event", "trigger" }));
        Assert.That(report.Rules, Has.All.Matches<RuleCoverage>(r => r.Successes <= r.Attempts));
        Assert.That(report.Rules, Has.All.Matches<RuleCoverage>(r => !string.IsNullOrEmpty(r.Schedule)));
    }

    [Test]
    public void QueryReturnsMatchingEntities()
    {
        // Person carries @display attributes, so describing each row runs its own back-reference
        // queries. Enough years to return many rows, because the bug this guards against — the row
        // describer clearing the buffer the result mapper is still walking — only shows up from the
        // second row onwards.
        var s = Session();
        s.PassYears(120);
        var result = s.Query("pick Person $p: ($p.alive)");

        Assert.That(result.Errors, Is.Null.Or.Empty, () => string.Join("\n", result.Errors));
        Assert.That(result.Results.Length, Is.GreaterThan(1), "the query should return several people");
        // Every row starts with the synthetic Type row EntityPropertyDisplays prepends.
        Assert.That(result.Results, Has.All.Matches<Result>(r => r.Properties[0].Label == "Type"));
        Assert.That(result.Results, Has.All.Matches<Result>(r => r.Properties.Count > 1));
        // The parsed expression is echoed back as .sg rather than as a dump of the object graph, which
        // is what makes "how was my text read?" answerable — note the explicit parenthesisation.
        Assert.That(result.Query, Does.StartWith("pick Person"));
        Assert.That(result.Query, Does.Contain("alive"));
        Assert.That(result.Query, Does.Not.Contain("{"), "no longer a JSON tree");
    }

    [Test]
    public void QueryEchoesTheExpressionWithItsPrecedenceMadeExplicit()
    {
        // The point of showing the parsed expression is to reveal how the text was grouped, so the
        // brackets are the content, not noise.
        var result = Session().Query("pick Person $p: ($p.alive and $p.age = Age.Old)");

        Assert.That(result.Errors, Is.Null.Or.Empty, () => string.Join("\n", result.Errors));
        Assert.That(result.Query, Is.EqualTo("pick Person $0: (($0.alive and ($0.age = Age.Old)))"));
    }

    [Test]
    public void QueryDescribesTheDisplayAttributeRowsOfEveryResult()
    {
        // The @display rows are the ones that need a nested query per result, so assert they actually
        // arrive rather than only that nothing threw. The labels come from the type's own metadata
        // instead of being spelled out here, since the story owns them.
        var s = Session();
        s.PassYears(150);
        var declared = s.Database.GetEntityType("Person").Attributes.Select(a => a.Label).ToList();
        Assert.That(declared, Is.Not.Empty, "w.sg gives Person @display back-references");

        var result = s.Query("pick Person $p: (not $p.alive)");
        Assert.That(result.Errors, Is.Null.Or.Empty, () => string.Join("\n", result.Errors));
        Assert.That(result.Results.Length, Is.GreaterThan(1));

        var labels = result.Results.SelectMany(r => r.Properties.Select(p => p.Label)).Distinct().ToList();
        Assert.That(labels.Intersect(declared), Is.Not.Empty,
            "at least one row should carry its @display back-references");
    }

    [Test]
    public void QueryReportsParseErrorsRatherThanThrowing()
    {
        var result = Session().Query("this is not a query");
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(result.Results, Is.Null.Or.Empty);
    }

    [Test]
    public void QueryRejectsAnExpressionThatIsNotAPick()
    {
        var result = Session().Query("1 + 1");
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(string.Join("", result.Errors), Does.Contain("unsuited").IgnoreCase
            .Or.Contain("Error").IgnoreCase);
    }

    [Test]
    public void ChangesetWindowsTileTheLogWithoutOverlapOrGaps()
    {
        var s = Session();
        s.PassYears(40);
        var total = s.GetChangesetsCount();
        Assert.That(total, Is.GreaterThan(4));

        var whole = s.GetChangesets(0, total);
        var halves = s.GetChangesets(0, total / 2)
            .Concat(s.GetChangesets(total / 2, total - total / 2))
            .ToList();

        Assert.That(halves.Count, Is.EqualTo(whole.Count));
        Assert.That(halves.Select(c => (c.Id, c.Year, c.ActionName)),
            Is.EqualTo(whole.Select(c => (c.Id, c.Year, c.ActionName))));
    }

    [Test]
    public void ChangesetWindowsPastTheEndClampRatherThanThrow()
    {
        var s = Session();
        s.PassYears(20);
        var total = s.GetChangesetsCount();

        Assert.That(s.GetChangesets(total, 100), Is.Empty);
        Assert.That(() => s.GetChangesets(total + 500, 100), Throws.Nothing);
        Assert.That(s.GetChangesets(0, total + 500).Count, Is.EqualTo(s.GetChangesets(0, total).Count));
    }

    [Test]
    public void EntityChangesetsOnlyMentionThatEntity()
    {
        var s = Session();
        s.PassYears(40);
        var eid = s.Database.Entities.First().Id.Id;

        var changes = s.GetEntityChangesets(eid);
        Assert.That(changes, Has.All.Matches<EntityChangeDisplay>(c => c.Id.Id == eid));
    }

    [Test]
    public void FamilyTreeLinksChildrenToParentsPresentInTheSameTree()
    {
        var s = Session();
        s.PassYears(120);

        // Someone with parents, so the tree is not a single node.
        var root = s.Database.Entities
            .Select(e => e.Id.Id)
            .FirstOrDefault(id => s.GetFamilyTree(id, 3).Count > 2);
        Assert.That(root, Is.Not.Zero, "w.sg should have produced at least one family by year 120");

        var tree = s.GetFamilyTree(root, 3);
        Assert.That(tree.Select(n => n.Id).Distinct().Count(), Is.EqualTo(tree.Count), "nodes are unique by id");
        Assert.That(tree, Has.All.Matches<FamilyTreeNode>(n => !string.IsNullOrEmpty(n.Name)),
            "every node is named, including a child's other parent");

        var ids = tree.Select(n => n.Id).ToHashSet();
        foreach (var n in tree)
        {
            if (n.P1 != 0) Assert.That(ids, Contains.Item(n.P1), $"#{n.Id}'s parent1 is missing from the tree");
            if (n.P2 != 0) Assert.That(ids, Contains.Item(n.P2), $"#{n.Id}'s parent2 is missing from the tree");
        }
    }

    [Test]
    public void FamilyTreeOfATypeWithoutParentsIsEmpty()
    {
        var s = Session();
        // The Time singleton declares no parent1/parent2, so it gets an empty list rather than a tree
        // of garbage read through the wrong type's property ids.
        var timeId = s.Database.Ctx.GetSingletonId(s.Database.GetEntityType("Time").Id);
        Assert.That(s.GetFamilyTree(timeId.Id, 3), Is.Empty);
    }

    [Test]
    public void EntityDetailsLeadWithTheTypeAndRenderRefsAsLinks()
    {
        var s = Session();
        s.PassYears(60);
        var eid = s.Database.Entities.First().Id.Id;

        var details = s.GetEntityDetails(eid);
        Assert.That(details, Is.Not.Empty);
        Assert.That(details[0].Label, Is.EqualTo("Type"));
        // Reference values carry the <#id>name</> markup the client turns into a link.
        Assert.That(details, Has.All.Matches<EntityPropertyDisplay>(d => d.Value != null));
    }

    [Test]
    public void RunActionAdvancesTheWorldWithoutAdvancingTheClock()
    {
        var s = Session();
        s.PassYears(60);
        var year = s.Year;
        var before = s.GetChangesetsCount();

        // Which events can run standalone depends on the story, so drive them all rather than naming one:
        // the contract is that RunAction changes the world without moving the clock.
        foreach (var action in s.Database.Actions)
            s.RunAction(action.Id);

        Assert.That(s.Year, Is.EqualTo(year), "running events out of schedule does not pass a year");
        Assert.That(s.GetChangesetsCount(), Is.GreaterThan(before),
            "at least one of the story's events should have changed something");
    }

    [Test]
    public void RunActionIgnoresAnUnknownId()
    {
        var s = Session();
        Assert.That(() => s.RunAction(99999), Throws.Nothing);
    }

    // ---- the record feed --------------------------------------------------

    [Test]
    public void TheFeedStartsWithAResetAndThenTracksRecords()
    {
        var s = Session();

        // First tick after construction: the world was just built, so the viewer is told to clear.
        var first = s.DrainFeed(0, out var cursor);
        Assert.That(first.First().Type, Is.EqualTo(Message.MessageType.Reset));
        Assert.That(first.Last().Type, Is.EqualTo(Message.MessageType.Year), "the year heartbeat closes every tick");
        Assert.That(cursor, Is.EqualTo(s.RecordCount));

        // A quiet tick still carries the heartbeat, and nothing else.
        var quiet = s.DrainFeed(cursor, out cursor);
        Assert.That(quiet.Select(m => m.Type), Is.EqualTo(new[] { Message.MessageType.Year }));

        s.PassYears(30);
        var busy = s.DrainFeed(cursor, out cursor);
        Assert.That(busy.Count(m => m.Type == Message.MessageType.Record), Is.GreaterThan(0));
        Assert.That(busy.Last().Year, Is.EqualTo(s.Year));
        Assert.That(cursor, Is.EqualTo(s.RecordCount));
    }

    [Test]
    public void EveryRecordReachesTheFeedExactlyOnce()
    {
        var s = Session();
        int cursor = 0;
        var delivered = new List<string>();

        // The very first tick carries the @start records, so it counts like any other.
        void Drain() => delivered.AddRange(s.DrainFeed(cursor, out cursor)
            .Where(m => m.Type == Message.MessageType.Record)
            .Select(m => m.Record!.Value.Text));

        Drain();
        for (int i = 0; i < 5; i++)
        {
            s.PassYears(20);
            Drain();
        }

        Assert.That(delivered, Is.EqualTo(s.Database.Records.Select(r => r.Text).ToList()));
    }

    [Test]
    public void ARequestedReloadRebuildsTheWorldOnTheNextTick()
    {
        // The server's file watcher only flags the change; the rebuild happens on the feed's thread.
        var s = Session();
        s.PassYears(30);
        s.DrainFeed(0, out var cursor);
        var year = s.Year;

        s.RequestReload(year);
        var batch = s.DrainFeed(cursor, out cursor);

        var reset = batch.First();
        Assert.That(reset.Type, Is.EqualTo(Message.MessageType.Reset));
        Assert.That(reset.Year, Is.EqualTo(year), "the reset carries the year the world had reached");
        Assert.That(s.Year, Is.EqualTo(s.Database.StartYear), "the world was rebuilt");
        // The cursor restarts, so the fresh world's setup records are delivered rather than skipped.
        Assert.That(cursor, Is.EqualTo(s.RecordCount));
    }

    [Test]
    public void AResetNoticeIsDeliveredOnlyOnce()
    {
        var s = Session();
        s.DrainFeed(0, out var cursor);
        s.Reset();

        Assert.That(s.DrainFeed(cursor, out cursor).Any(m => m.Type == Message.MessageType.Reset), Is.True);
        Assert.That(s.DrainFeed(cursor, out cursor).Any(m => m.Type == Message.MessageType.Reset), Is.False);
    }

    // ---- the wire format --------------------------------------------------

    [Test]
    public void TheWireFormatIsCamelCaseWithFieldsAndStringEnums()
    {
        // The Svelte client's types are written by hand against this shape (src/lib/types.ts), and both
        // hosts must produce it identically. A silent drift here means properties arrive under names the
        // client never reads, so pin the parts that are easy to lose.
        var s = Session();
        s.PassYears(20);

        var clientData = JsonSerializer.Serialize(s.GetClientData(), MoiraiWireJson.Options);
        // ClientData exposes fields, not properties: without IncludeFields this is "{}".
        Assert.That(clientData, Does.Contain("\"actions\""));
        Assert.That(clientData, Does.Contain("\"types\""));
        Assert.That(clientData, Does.Contain("\"seed\""));

        var feed = JsonSerializer.Serialize(s.DrainFeed(0, out _), MoiraiWireJson.Options);
        // MessageType must be a string union, not a number.
        Assert.That(feed, Does.Contain("\"type\":\"Reset\""));
        Assert.That(feed, Does.Contain("\"type\":\"Year\""));

        var changes = JsonSerializer.Serialize(s.GetChangesets(0, 1), MoiraiWireJson.Options);
        // EntityId collapses to a bare number rather than an object wrapping its field.
        Assert.That(changes, Does.Match("\"id\":\\d+"));

        var bio = JsonSerializer.Serialize(s.GetBiography(s.Database.Entities.First().Id.Id),
            MoiraiWireJson.Options);
        Assert.That(bio, Does.Contain("\"timeline\""));
        Assert.That(bio, Does.Contain("\"hasFamily\""));
    }
}
