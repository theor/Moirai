using System.Collections.Immutable;
using Moirai.Core;
using Moirai.Parser;

namespace Moirai.Api;

/// <summary>
/// One Moirai world plus every question a viewer can ask of it — the whole client API surface, with no
/// transport attached. The SignalR hub and the WebAssembly export are both thin shims over this class.
///
/// <para><b>Not thread-safe, by design.</b> A world is a mutable object graph and a simulation pass walks
/// all of it, so concurrent access has to be excluded by whoever owns the session. The server does that
/// with a single semaphore around each call; the browser needs nothing, because its runtime is
/// single-threaded. Putting the lock here instead would force the browser to pay for a problem it cannot
/// have, and would make the reentrancy rules depend on which host you were reading.</para>
///
/// <para>A session is identified by its story text plus its seed: the simulation is deterministic per
/// seed, so those two are the whole identity of a run.</para>
/// </summary>
public sealed class WorldSession
{
    // Not readonly: SetStory swaps it, which is how the browser edits the world it is looking at.
    private Func<string> _storyText;
    private readonly bool _profiling;

    private Database _db;
    private ulong _seed;

    // "A reset happened, tell the feed." Read and cleared by TakeResetNotice.
    private bool _resetNotice;
    // "The story changed on disk, rebuild on the next feed tick." Server-only; see RequestReload.
    private bool _reloadRequested;
    // The year the world was at when a reload was requested, so the viewer can show where it was.
    private long? _targetYear;

    // Scratch for FindAll, which fills a caller-owned list rather than allocating per query. Instance
    // fields rather than static ones: two sessions sharing them would corrupt each other's results.
    //
    // There are two because FindAll *clears* the list it is handed, and describing a row runs its own
    // queries — one per @display back-reference on the type. One shared buffer would mean the callee
    // emptying the list the caller is still walking, which throws on the second row and made any type
    // with a @display attribute (Person, in w.sg) unqueryable. Splitting them removes the hazard rather
    // than relying on no one nesting.
    private List<EntityId> _queryResults = new();
    private List<EntityId> _displayResults = new();

    /// <summary>
    /// Build a world and run its <c>@start</c> events. <paramref name="storyText"/> is re-invoked on every
    /// <see cref="Reset"/>, so a host that watches a file on disk picks up edits for free.
    /// </summary>
    public WorldSession(Func<string> storyText, ulong seed = 42, bool profiling = false)
    {
        _storyText = storyText;
        _seed = seed;
        _profiling = profiling;
        _db = null!;
        Reset();
    }

    public WorldSession(string storyText, ulong seed = 42, bool profiling = false)
        : this(() => storyText, seed, profiling)
    {
    }

    /// <summary>The live world. Exposed for the debug adapter, which drives the engine directly.</summary>
    public Database Database => _db;

    /// <summary>A step-through debugger observing this world, or null. Survives <see cref="Reset"/>.</summary>
    public IDebugHook? DebugHook
    {
        get => _db.DebugHook;
        set
        {
            _attachedHook = value;
            _db.DebugHook = value;
        }
    }

    private IDebugHook? _attachedHook;

    // What the parser said about the story the current world was built from. Warnings survive a
    // successful build, so an editor can still show them once the world is up.
    private StoryDiagnostic[] _diagnostics = Array.Empty<StoryDiagnostic>();

    // ---- lifecycle ---------------------------------------------------------

    /// <summary>Rebuild the world from the story text. Returns the year of the fresh world.</summary>
    public long Reset()
    {
        // This order is load-bearing. SetSeed must precede Init, because @start events run inside Init
        // and have to draw from the requested seed; and History must exist before Init so the setup
        // changesets land in the log the World and Life pages replay.
        _db = StoryParser.Parse(_storyText(), out var errors);
        _diagnostics = Diagnose(errors);
        _db.History = new History();
        _db.ProfilingEnabled = _profiling;
        _db.SetSeed(_seed);
        _db.Init();
        _db.DebugHook = _attachedHook;
        _resetNotice = true;
        return _db.Ctx.Year;
    }

    /// <summary>Rebuild the world from a different seed. Returns the year of the fresh world.</summary>
    public long Reseed(ulong seed)
    {
        _seed = seed;
        return Reset();
    }

    /// <summary>The seed the current world was built with.</summary>
    public ulong GetSeed() => _seed;

    // ---- the story ---------------------------------------------------------
    //
    // Editing the story from the viewer is a browser-only affair: the server's story is a file on disk
    // and its watcher owns it, so a host that has one should not offer these. They live here anyway
    // because the rule holds that WorldSession is the whole client API and nothing duplicates it.

    /// <summary>The story text the current world was built from.</summary>
    public string GetStory() => _storyText();

    /// <summary>Everything the parser said about the story the current world was built from.</summary>
    public StoryDiagnostic[] GetStoryDiagnostics() => _diagnostics;

    /// <summary>
    /// Parse <paramref name="text"/> without touching the live world, and report what the parser found.
    /// This is what an editor calls as you type.
    /// </summary>
    public StoryDiagnostic[] ValidateStory(string text)
    {
        // Database.Instance is a mutable static, assigned by every Database constructor and read by
        // *live* simulation code -- Changeset clones an entity through Database.Instance.GetEntityType.
        // Parsing a throwaway world would leave it pointing at the throwaway, so the world being
        // validated against would start describing its entities with another story's types. Putting it
        // back is the whole reason this method is not a one-liner.
        var live = Database.Instance;
        try
        {
            StoryParser.Parse(text, out var errors);
            return Diagnose(errors);
        }
        catch (Exception e)
        {
            return [Fatal(e)];
        }
        finally
        {
            Database.Instance = live;
        }
    }

    /// <summary>
    /// Rebuild the world from a new story. Nothing happens if it does not parse: the world stays exactly
    /// as it was and the diagnostics say why. A story that does parse produces a fresh world at its start
    /// year, which is <see cref="Reset"/>'s behaviour and not a fast-forward -- the years the old world
    /// lived through were the old story's.
    /// </summary>
    public StoryApplyResult SetStory(string text)
    {
        var diagnostics = ValidateStory(text);
        if (Array.Exists(diagnostics, d => d.Severity == nameof(StoryParser.Severity.Error)))
            return new StoryApplyResult(false, Year, diagnostics);

        // A story can parse clean and still throw on the way up -- an @start event that picks from an
        // empty world, say. Keeping the old text means a session survives that instead of being left
        // with no world at all.
        var previous = _storyText;
        try
        {
            _storyText = () => text;
            Reset();
            return new StoryApplyResult(true, Year, _diagnostics);
        }
        catch (Exception e)
        {
            _storyText = previous;
            Reset();
            return new StoryApplyResult(false, Year, [Fatal(e)]);
        }
    }

    private static StoryDiagnostic[] Diagnose(List<StoryParser.Error> errors) =>
        errors.ConvertAll(e => new StoryDiagnostic(e.Severity.ToString(), e.Code.ToString(),
            e.Line, e.Col, e.LineEnd, e.ColEnd, e.Message)).ToArray();

    // An exception has no position, so it is reported against the first character rather than nowhere.
    private static StoryDiagnostic Fatal(Exception e) =>
        new(nameof(StoryParser.Severity.Error), nameof(StoryParser.ErrorCode.Exception), 1, 0, 1, 1,
            e.Message);

    /// <summary>Simulate forward. Synchronous: the caller decides what thread wears the cost.</summary>
    public void PassYears(int years, IProgress<int>? progress = null, CancellationToken ct = default) =>
        _db.Ctx.PassYears(years, ct, progress, true);

    /// <summary>Historically flushed the world to disk; now a no-op the engine keeps for callers.</summary>
    public void Save() => _db.Commit();

    // ---- the record feed ---------------------------------------------------
    //
    // A viewer follows the world by polling these rather than being pushed to, because the engine has no
    // notification of its own: a simulation pass just appends to Database.Records. Both hosts poll on the
    // same cadence and build the same Message sequence.

    public long Year => _db.Ctx.Year;

    public int RecordCount => _db.Records.Count;

    /// <summary>
    /// The next batch of feed messages after <paramref name="cursor"/> records have already been seen,
    /// plus the cursor to pass back next time. Servicing a pending reload is part of the tick, because
    /// this is the one call a host makes from a thread that is allowed to rebuild the world.
    /// </summary>
    public List<Message> DrainFeed(int cursor, out int newCursor)
    {
        var batch = new List<Message>();

        // A file edit only sets a flag; acting on it here keeps world rebuilds off the watcher thread.
        if (TakePendingReload())
            Reset();

        if (TakeResetNotice(out var targetYear))
        {
            batch.Add(Message.Reset(targetYear));
            cursor = 0;
        }

        for (int i = Math.Max(0, cursor); i < _db.Records.Count; i++)
            batch.Add(new Message(_db.Records[i]));
        newCursor = _db.Records.Count;

        // Always last, and always sent: the year is the heartbeat that tells a viewer the feed is alive
        // even in a stretch of history where nothing was recorded.
        batch.Add(Message.YearMessage(Year));
        return batch;
    }

    /// <summary>
    /// Note that the story changed underneath us and the world should be rebuilt on the next feed tick.
    /// Deferred rather than immediate so a file-watcher thread never rebuilds the world out from under a
    /// simulation pass. <paramref name="targetYear"/> is where the world had got to, so the viewer can
    /// report it.
    /// </summary>
    public void RequestReload(long? targetYear)
    {
        _reloadRequested = true;
        _targetYear = targetYear;
    }

    private bool TakePendingReload()
    {
        if (!_reloadRequested) return false;
        _reloadRequested = false;
        return true;
    }

    private bool TakeResetNotice(out long? targetYear)
    {
        targetYear = null;
        if (!_resetNotice) return false;
        _resetNotice = false;
        targetYear = _targetYear;
        _targetYear = null;
        return true;
    }

    // ---- queries ----------------------------------------------------------

    public ClientData GetClientData() => new()
    {
        Actions = _db.Actions.Select(a => new ActionData(a.Id, a.Name)).ToArray(),
        Types = _db.Types.Skip(1).Select(a => new TypeData((int)a.Id.Id, a.Name)).OrderBy(x => x.Name).ToArray(),
        Seed = _seed,
    };

    /// <summary>
    /// Everything the world knows about one entity, on one page: its current state, and its whole life
    /// as a single ordered timeline. The three sources — records, changesets and the family tree — were
    /// each reachable before, on three different pages that did not know about each other.
    /// </summary>
    public Biography GetBiography(uint eid)
    {
        if (!_db.TryGetEntity(new EntityId(eid), out var entity))
            return new Biography(eid, "", "", false, Array.Empty<EntityPropertyDisplay>(),
                Array.Empty<BiographyEntry>());

        var type = _db.GetEntityType(entity.Type);
        var name = entity.TryGetProperty(Database.PropName, out var n) ? n.Value ?? "" : entity.Id.ToString();

        var entries = new List<BiographyEntry>();

        // Setup effects run inside Init() before Time exists, so their changesets and records carry
        // year 0 while the world actually begins at StartYear (764 in w.sg). Clamping puts them at
        // the start of the life instead of an orphan "year 0" heading centuries before it.
        long Begins(long year) => Math.Max(year, _db.StartYear);

        foreach (var r in _db.Records)
            if (MentionsEntity(r, eid))
                entries.Add(new BiographyEntry(Begins(r.Year), r.ChangesetId, "record", r.Text,
                    ActionName(r.ActionId), Array.Empty<EntityPropertyDisplay>(),
                    r.Tags ?? Array.Empty<string>()));

        if (_db.History != null)
            foreach (var cs in _db.History.Changesets)
            foreach (var change in cs.Changes)
                if (change.New.Id.Id == eid)
                    entries.Add(new BiographyEntry(Begins(cs.Year), cs.Id, "change", "", cs.ActionName,
                        GetChangeDetails(change), Array.Empty<string>()));

        // A stable sort, not List.Sort: an event and the triggers it fires share a changeset id (see
        // RunTriggers), so ties are common, and an unstable order would reshuffle a life every time
        // the page refreshed.
        var ordered = entries
            .OrderBy(e => e.Year)
            .ThenBy(e => e.ChangesetId)
            .ToArray();

        return new Biography(eid, name, type.Name, HasParents(type), EntityPropertyDisplays(eid), ordered);
    }

    /// <summary>
    /// Headline counts plus the always-meaningful series: narrative volume, world activity, and one
    /// cumulative entity count per type. All of it is replayed from the changeset log by
    /// <see cref="WorldSeries"/>, so the simulation pays nothing for it.
    /// </summary>
    public WorldOverview GetWorldOverview()
    {
        if (_db.History == null)
            return new WorldOverview(0, 0, 0, 0, Array.Empty<TimeSeries>(), Array.Empty<ChartableProperty>());

        var series = new List<TimeSeries>
        {
            WorldSeries.RecordsPerYear(_db),
            WorldSeries.ChangesPerYear(_db),
        };
        foreach (var type in WorldSeries.StoryTypes(_db))
            if (WorldSeries.EntitiesOfType(_db, type) is { } s)
                series.Add(s);

        var properties = WorldSeries.Chartable(_db)
            .Select(c => new ChartableProperty((int)c.Type.Id.Id, c.Type.Name, c.Property.Name,
                c.IsBool ? "bool" : "number"))
            .ToArray();

        return new WorldOverview(Math.Max(0, _db.Ctx.Year), _db.Entities.Count(), _db.Records.Count,
            _db.History.Changesets.Count, series.ToArray(), properties);
    }

    /// <summary>One property's history, replayed from the changeset log. See <see cref="WorldSeries.PropertyOverTime"/>.</summary>
    public TimeSeries GetPropertySeries(int typeId, string propertyName)
    {
        if (_db.History == null)
            return TimeSeries.Empty;
        return WorldSeries.PropertyOverTime(_db, _db.GetEntityType(new EntityTypeId((uint)typeId)), propertyName);
    }

    /// <summary>
    /// Firing counts for every event and trigger in the story. A rule with zero attempts has never run:
    /// for a scheduled event that means its frequency never came due, for a trigger that nothing it
    /// watches ever changed (or property gating always excluded it). A rule with attempts but no
    /// successes always aborted — an event whose <c>pick</c> finds nothing, or a trigger whose predicate
    /// never matched. Both are silent story bugs the records feed cannot show you.
    /// </summary>
    public RuleCoverageReport GetRuleCoverage()
    {
        var rules = _db.Actions
            .Select(a => new RuleCoverage(a.Id, a.Name, "event", DescribeSchedule(a), a.Attempts,
                a.Successes, a.Tags?.ToArray() ?? Array.Empty<string>()))
            .Concat(_db.Triggers
                .Select(t => new RuleCoverage(t.Id, t.Name, "trigger", DescribeWhen(t), t.Attempts,
                    t.Successes, t.Tags?.ToArray() ?? Array.Empty<string>())))
            .ToArray();
        return new RuleCoverageReport(_db.Ctx.Year, rules);
    }

    public QueryResult Query(string q)
    {
        string? sql = null;
        try
        {
            AstVisitor v = new AstVisitor(_db);
            var e = StoryParser.ParseExpr(v, q, 0, 0, out var errors);
            if (errors.Any())
                return new QueryResult { Errors = errors.Select(error => error.ToString()).ToArray() };
            if (e is AssignPick pick)
            {
                _db.FindAll(pick.EntityType, pick.Value, pick.VariableIndex, ref _queryResults, out sql);
                var ids = _queryResults.ToArray();

                return new QueryResult
                {
                    Sql = sql,
                    // The engine's own printer, not a JSON dump of the object graph. It renders the
                    // parsed expression back as .sg with the precedence made explicit, which answers
                    // "how was my text read?" better than a tree of node types — and it is the last
                    // reflective serialization in the product, which is what kept the WebAssembly build
                    // from being trimmed.
                    Query = _db.Printer.Print(e).TrimEnd(),
                    Results = Array.ConvertAll(ids, eid => new Result
                    {
                        Eid = eid, Properties = EntityPropertyDisplays(eid.Id),
                    })
                };
            }

            return new QueryResult { Errors = new[] { "Instruction unsuited for query: " + e!.GetType() } };
        }
        catch (Exception e)
        {
            return new QueryResult { Sql = sql, Errors = new[] { e.ToString() } };
        }
    }

    public void RunAction(int actionId)
    {
        var eventTrigger = _db.Actions.FirstOrDefault(a => a.Id == actionId);
        if (eventTrigger != null)
            _db.RunAction(eventTrigger);
    }

    public int GetChangesetsCount() => _db.History?.Changesets.Count ?? 0;

    /// <summary>
    /// A window of the changeset log, flattened to one row per changed entity. Materialized rather than
    /// returned lazily: the caller holds the world still only for the duration of the call, so a lazy
    /// chain would be walked after that guarantee expired.
    /// </summary>
    public List<EntityChangeDisplay> GetChangesets(int start, int count)
    {
        if (_db.History == null)
            return new List<EntityChangeDisplay>();
        var all = _db.History.Changesets;
        return all.Skip(start).Take(Math.Min(count, Math.Max(0, all.Count - start)))
            .SelectMany(GetChangesetDetails)
            .ToList();
    }

    public List<EntityChangeDisplay> GetEntityChangesets(uint eid)
    {
        if (_db.History == null)
            return new List<EntityChangeDisplay>();
        return _db.History.Changesets
            .SelectMany(cs => cs.Changes
                .Where(x => x.New.Id.Id == eid)
                .Select(x => new EntityChangeDisplay(x.New.Id, cs.Year, cs.ActionName, GetChangeDetails(x))))
            .ToList();
    }

    public List<FamilyTreeNode> GetFamilyTree(uint eid, int maxDepth)
    {
        HashSet<FamilyTreeNode> nodes = new();

        // The tree is built against the root entity's own type, not a hardcoded Person: any type
        // declaring parent1/parent2 gets a genealogy, and one that doesn't gets an empty list
        // instead of a tree of garbage read through the wrong type's property ids.
        if (!_db.TryGetEntity(new EntityId(eid), out var root))
            return new List<FamilyTreeNode>();
        var rootType = _db.GetEntityType(root.Type);
        var prop1 = rootType.GetPropertyId("parent1");
        var prop2 = rootType.GetPropertyId("parent2");
        if (!prop1.IsValid || !prop2.IsValid)
            return new List<FamilyTreeNode>();

        Queue<(EntityId id, int depth)> queue = new();
        queue.Enqueue((new EntityId(eid), 0));
        while (queue.TryDequeue(out var item))
        {
            if (!_db.TryGetEntity(item.id, out Entity e))
                continue;
            var node = new FamilyTreeNode(e.Id.Id,
                e.TryGetProperty(Database.PropName, out var name) ? name.Value! : e.Id.ToString(),
                item.depth >= maxDepth ? 0 : e.TryGetProperty(prop1, out var p1) ? p1.Id.Id : 0,
                item.depth >= maxDepth ? 0 : e.TryGetProperty(prop2, out var p2) ? p2.Id.Id : 0
            );
            if (node.P1 != 0)
                queue.Enqueue((new EntityId(node.P1), item.depth + 1));
            if (node.P2 != 0)
                queue.Enqueue((new EntityId(node.P2), item.depth + 1));
            nodes.Add(node);
        }

        // Children: everyone whose parent1/parent2 is the root. The predicate reads the candidate
        // through a real value-stack slot inside its own scope — the old -1 "no variable" index is
        // retired, and FindAll binding it would index the value stack at -1 and throw.
        const int queryVar = 0;
        using (_db.Ctx.RunScope(true))
        {
            _db.FindAll(rootType.Id,
                new BinaryOperator(BinaryOperator.Operator.Or,
                    new BinaryOperator(BinaryOperator.Operator.Equals, new PropertyPath(queryVar, rootType.RefType, prop1), new Literal(new EntityId(eid))),
                    new BinaryOperator(BinaryOperator.Operator.Equals, new PropertyPath(queryVar, rootType.RefType, prop2), new Literal(new EntityId(eid)))
                ), queryVar, ref _queryResults);
        }

        foreach (var id in _queryResults)
            queue.Enqueue((id, 0));
        while (queue.TryDequeue(out var item))
        {
            if (!_db.TryGetEntity(item.id, out Entity e))
                continue;
            var p1Id = item.depth >= maxDepth ? 0 : e.TryGetProperty(prop1, out var p1) ? p1.Id.Id : 0;
            var p2Id = item.depth >= maxDepth ? 0 : e.TryGetProperty(prop2, out var p2) ? p2.Id.Id : 0;
            nodes.Add(new FamilyTreeNode(e.Id.Id,
                e.TryGetProperty(Database.PropName, out var name) ? name.Value! : e.Id.ToString(),
                p1Id,
                p2Id
            ));
            // A child's other parent is not in the ancestor sweep, so name it here rather than let
            // the client show an unnamed node. Add is a no-op when the id is already known, so this
            // never overwrites a node the sweep built (FamilyTreeNode equality is the id alone).
            AddCoParent(nodes, p1Id);
            AddCoParent(nodes, p2Id);
        }

        return nodes.ToList();
    }

    public IList<EntityPropertyDisplay> GetEntityDetails(uint eid) => EntityPropertyDisplays(eid);

    // ---- helpers ----------------------------------------------------------

    // Participants are collected from the record's {$var} interpolation slots. Records emitted before a
    // rule bound any variable have none, so fall back to the entity marker the printer writes into the
    // text — the same fallback the records feed uses for its per-entity filter.
    private static bool MentionsEntity(Database.Record r, uint eid) =>
        r.Participants is { Length: > 0 }
            ? r.Participants.Any(p => p.Id == eid)
            : r.Text.Contains($"<#{eid}>", StringComparison.Ordinal);

    private static bool HasParents(EntityType type) =>
        type.GetPropertyId("parent1").IsValid && type.GetPropertyId("parent2").IsValid;

    private string ActionName(int actionId) =>
        _db.Actions.FirstOrDefault(a => a.Id == actionId)?.Name
        ?? _db.Triggers.FirstOrDefault(t => t.Id == actionId)?.Name
        ?? "";

    private static string DescribeSchedule(EventTrigger e) => e.Filter switch
    {
        null => "call only",
        FilterAtStart => "@start",
        FilterExactlyXEveryYYears f => $"{f.Count}× every {f.Years}y",
        FilterProbabilityXPerYears f => $"~{f.Event.ExpectedOccurences}× per {f.Event.ExpectedInterval}y",
        _ => e.Filter.GetType().Name,
    };

    private string DescribeWhen(EventTrigger t)
    {
        var (whenType, typeId, predicate) = t.When;
        var typeName = _db.GetEntityType(typeId).Name;
        var keyword = whenType == EventTrigger.WhenType.Created ? "when_created" : "when";
        return predicate == null ? $"{keyword} {typeName}" : $"{keyword} {typeName} and …";
    }

    private void AddCoParent(HashSet<FamilyTreeNode> nodes, uint id)
    {
        if (id == 0 || !_db.TryGetEntity(new EntityId(id), out var e))
            return;
        nodes.Add(new FamilyTreeNode(id,
            e.TryGetProperty(Database.PropName, out var name) ? name.Value! : e.Id.ToString(), 0, 0));
    }

    private IList<EntityChangeDisplay> GetChangesetDetails(Changeset cs) =>
        cs.Changes.Select(x => new EntityChangeDisplay(x.New.Id, cs.Year, cs.ActionName, GetChangeDetails(x)))
            .ToList();

    private IList<EntityPropertyDisplay> GetChangeDetails(Changeset.Changed c)
    {
        if (c.Prev.Id.IsNull) // new entity
        {
            return c.New.Properties.Where(p => p.Id.IsValid)
                .Select(p => new EntityPropertyDisplay(_db.GetPropertyName(p.Id), PrintValue(p.Id, p.Value)))
                .ToList();
        }

        return c.Prev.Properties.Where(p => p.Id.IsValid)
            .Select(p =>
            {
                var p1 = c.New.GetProperty(p.Id);
                return new EntityPropertyDisplay(_db.GetPropertyName(p.Id),
                    PrintValue(p.Id, p.Value) + " -> " + PrintValue(p.Id, p1));
            }).ToList();
    }

    private IList<EntityPropertyDisplay> EntityPropertyDisplays(uint eid)
    {
        if (!_db.TryGetEntity(new EntityId(eid), out var e))
        {
            return ImmutableList<EntityPropertyDisplay>.Empty;
        }

        var details = e.Properties.Where(p => p.Id.IsValid)
            .Select(p => new EntityPropertyDisplay(
                _db.GetPropertyName(p.Id),
                PrintValue(p.Id, p.Value))).ToList();
        var t = _db.GetEntityType(e.Type);
        details.Insert(0, new EntityPropertyDisplay("Type", t.Name));
        foreach (var display in t.Attributes)
        {
            using var _ = _db.Ctx.RunScope(false);
            _db.Ctx.SetArgument(display.VarIndex, e.Id);
            _db.FindAll(display.ReferencedType.Id, display.Value, display.OtherVarIndex, ref _displayResults);
            foreach (var id in _displayResults)
            {
                if (!_db.TryGetEntity(id, out var ee)) continue;

                _db.Ctx.SetArgument(display.OtherVarIndex, id);
                details.Add(new EntityPropertyDisplay(display.Label,
                    $"<{ee.Id}>{(_db.GetProperty(ee.Id, Database.PropName, out var val) ? val.Value : ee.Id)}</>" +
                    (display.ItemDisplay == null ? "" : _db.Printer.Format(display.ItemDisplay, _db, true))));
            }
        }

        return details;
    }

    // Reference values render as the <#id>name</> markup the client turns into a link (see
    // ClientAppSvelte/src/lib/utils.ts). Everything else prints as itself.
    private string PrintValue(PropertyId propertyId, PropertyValue propertyValue)
    {
        var print = _db.Printer.Print(propertyValue);
        if (_db.GetPropertyType(propertyId, out var type) && type.IsRefType)
        {
            return propertyValue.Id.IsNull
                ? "null"
                : $"<{print}>{(_db.GetProperty(propertyValue.Id, Database.PropName, out var val) ? val.Value : print)}</>";
        }

        return print;
    }
}
