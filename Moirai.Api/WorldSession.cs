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
    /// Every entity's type id, indexed by entity id (index 0 is unused and holds 0). Record text links an
    /// entity by id and name only, so this is how a viewer tells a person from a country at a glance
    /// without asking the world once per link.
    /// </summary>
    public int[] GetEntityTypes()
    {
        var types = new List<int> { 0 };
        foreach (var e in _db.Entities)
        {
            while (types.Count < e.Id.Id) types.Add(0);
            types.Add((int)e.Type.Id);
        }
        return types.ToArray();
    }

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

        // A story's @start events run before it sets Time.year, and whatever they record before that
        // line carries year 0 (w.sg sets it first, so nothing does). Clamping puts such entries at the
        // start of the life instead of an orphan "year 0" heading centuries before it.
        long Begins(long year) => Math.Max(year, _db.StartYear);

        foreach (var r in _db.Records)
            if (MentionsEntity(r, eid))
                entries.Add(new BiographyEntry(Begins(r.Year), r.ChangesetId, "record", r.Text,
                    r.Rule ?? ActionName(r.ActionId), Array.Empty<EntityPropertyDisplay>(),
                    r.Tags ?? Array.Empty<string>(), r.Firing));

        if (_db.History != null)
            foreach (var cs in _db.History.Changesets)
            foreach (var change in cs.Changes)
                if (change.Id.Id == eid)
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
    /// The entities the story talks about most, grouped by type: for each type, the <paramref name="perType"/>
    /// entities mentioned in the most records. "Mentioned" is the same test the biography uses, so an
    /// entity's count here is the number of records on its Life page.
    ///
    /// <para>Singletons (Time) are left out: they are the world's furniture, not its characters.</para>
    /// </summary>
    public NotableGroup[] GetNotable(int perType) => Memo(nameof(GetNotable), perType, () => ComputeNotable(perType));

    private NotableGroup[] ComputeNotable(int perType)
    {
        var tally = new Dictionary<uint, (int Count, long First, long Last)>();
        var seen = new HashSet<uint>();
        void Tally(uint id, long year)
        {
            if (!seen.Add(id)) return;
            tally[id] = tally.TryGetValue(id, out var t) ? (t.Count + 1, t.First, year) : (1, year, year);
        }

        foreach (var r in _db.Records)
        {
            seen.Clear();
            // Mentioned's rule, with the participants read in place: building every record's array (and an
            // iterator per record) was most of what opening Home cost.
            var participants = r.ParticipantSpan();
            if (participants.Length > 0)
            {
                foreach (var p in participants)
                    if (!p.IsNull)
                        Tally(p.Id, r.Year);
            }
            else
                foreach (var id in Mentioned(r))
                    Tally(id, r.Year);
        }

        return tally
            .Select(kv => (Id: kv.Key, kv.Value.Count, kv.Value.First, kv.Value.Last,
                Found: _db.TryGetEntity(new EntityId(kv.Key), out var e), Entity: e))
            .Where(x => x.Found && !_db.GetEntityType(x.Entity.Type).IsSingleton)
            .GroupBy(x => x.Entity.Type.Id)
            .Select(g => (Type: _db.GetEntityType(g.First().Entity.Type), Total: g.Sum(x => x.Count),
                Top: g.OrderByDescending(x => x.Count).ThenByDescending(x => x.Last).ThenBy(x => x.Id)
                    .Take(perType)
                    .Select(x => new NotableEntity(x.Id, NameOf(x.Entity), x.Count, x.First, x.Last))
                    .ToArray()))
            .OrderByDescending(g => g.Total)
            .ThenBy(g => g.Type.Name, StringComparer.Ordinal)
            .Select(g => new NotableGroup((int)g.Type.Id.Id, g.Type.Name, HasParents(g.Type), g.Top))
            .ToArray();
    }

    /// <summary>
    /// The world at a glance, for the card a shared link opens on: its span, its population over time, its
    /// named eras and up to <paramref name="turningPoints"/> of its weightiest records.
    ///
    /// <para><b>Turning points</b> are shared out between <i>kinds</i> of record — one sentence at one weight —
    /// a slot at a time, heaviest kind first, and each kind's share is spread evenly across the years it
    /// happened in. Taking the heaviest records outright fills the card with whichever weighty thing is
    /// also frequent (eight vacant thrones); sharing gives eight different kinds of turning point, which
    /// is what a reader needs to see the shape of a history. Only records above
    /// <see cref="Database.Record.DefaultWeight"/> qualify, unless the story weighs nothing, in which case
    /// every ordinary record does.</para>
    ///
    /// <para><b>Population</b> is the type with the most entities among those declaring a bool
    /// <c>alive</c>, counted where it is true — the same convention the family tree reads death by. A
    /// story with no such type gets the cumulative count of its largest type instead.</para>
    ///
    /// <para><b>Tags</b> lose their quotes here: the parser keeps a @tag's literal text, quotes and all
    /// (see <c>AstVisitor</c>), and a card is the wrong place to show that.</para>
    /// </summary>
    public Chronicle GetChronicle(int turningPoints) =>
        Memo(nameof(GetChronicle), turningPoints, () => ComputeChronicle(turningPoints));

    private Chronicle ComputeChronicle(int turningPoints)
    {
        long Begins(long year) => Math.Max(year, _db.StartYear);
        static string[] Tags(string[]? tags) => tags?.Select(t => t.Trim('\'')).ToArray() ?? Array.Empty<string>();

        var weighted = _db.Records.Any(r => r.Weight != Database.Record.DefaultWeight);
        var threshold = weighted ? Database.Record.DefaultWeight + 1 : Database.Record.DefaultWeight;

        // Records are appended in time order, so each kind's list is already chronological; the stable
        // OrderBy keeps kinds of equal weight in the order they first happened.
        var kinds = _db.Records.Where(r => r.Weight >= threshold)
            .GroupBy(r => (Template(r.Text), r.Weight))
            .Select(g => g.ToList())
            .OrderByDescending(k => k[0].Weight)
            .ToList();
        var quota = new int[kinds.Count];
        for (int left = turningPoints, given = -1; left > 0 && given != 0;)
        {
            given = 0;
            for (int k = 0; k < kinds.Count && left > 0; k++)
                if (quota[k] < kinds[k].Count)
                {
                    quota[k]++;
                    left--;
                    given++;
                }
        }

        var chosen = new List<Database.Record>();
        for (int k = 0; k < kinds.Count; k++)
            for (int i = 0; i < quota[k]; i++)
                chosen.Add(kinds[k][(int)((i + 0.5) * kinds[k].Count / quota[k])]);

        var entries = chosen
            .OrderBy(r => r.Year).ThenBy(r => r.ChangesetId)
            .Select(r => new ChronicleEntry(Begins(r.Year), r.ChangesetId, r.Text, r.Weight, Tags(r.Tags)))
            .ToArray();

        // Counted per tag as the rules declare them (one array per rule, shared by its records), then merged
        // by the quote-trimmed name the card shows -- not a trimmed copy of every record's tags.
        var perRawTag = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in _db.Records)
            if (r.Tags != null)
                foreach (var t in r.Tags)
                    perRawTag[t] = perRawTag.GetValueOrDefault(t) + 1;
        var perTag = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (t, n) in perRawTag)
        {
            var name = t.Trim('\'');
            perTag[name] = perTag.GetValueOrDefault(name) + n;
        }
        var tags = perTag
            .Select(kv => new ChronicleTag(kv.Key, kv.Value))
            .OrderByDescending(t => t.Records).ThenBy(t => t.Tag, StringComparer.Ordinal)
            .ToArray();

        return new Chronicle(_db.StartYear, Math.Max(0, _db.Ctx.Year), _db.Records.Count, Population(),
            Eras(), entries, tags, weighted);
    }

    // A record's sentence with its entity links and numbers blanked: what one record() call site writes
    // whatever it is about. The rule is no key for this: a trigger's records carry the id of the event
    // that set it off (RunTriggers does not replace Database._currentActionId), so one succession
    // trigger would count as a different kind for every way a ruler can die.
    private static string Template(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '<' && i + 1 < text.Length && text[i + 1] == '#'
                && text.IndexOf("</>", i, StringComparison.Ordinal) is var close and >= 0)
            {
                sb.Append('_');
                i = close + 2;
            }
            else if (char.IsAsciiDigit(text[i]))
            {
                if (sb.Length == 0 || sb[^1] != '#') sb.Append('#');
            }
            else
                sb.Append(text[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The whole history as a Markdown document, chaptered by the story's ages, for a reader to keep: the
    /// engine's <see cref="StoryPrinter.ExportChronicle"/>, titled with what identifies this world.
    /// </summary>
    public string GetChronicleMarkdown() =>
        _db.Printer.ExportChronicle(
            $"Chronicle of seed {_seed}, {_db.StartYear}–{Math.Max(_db.StartYear, _db.Ctx.Year)}");

    private TimeSeries Population()
    {
        if (_db.History == null)
            return TimeSeries.Empty;

        // The story's @population if it named one; otherwise the largest type that can die (an alive or
        // dead role); otherwise the largest type, counted as everyone who ever lived.
        int Count(EntityType t) => _db.Entities.Count(e => e.Type == t.Id);
        var types = WorldSeries.StoryTypes(_db).Where(t => !t.IsSingleton).ToList();
        static bool Mortal(EntityType t) => t.Role(EntityRole.Alive).IsValid || t.Role(EntityRole.Dead).IsValid;
        var chosen = types.FirstOrDefault(t => t.IsPopulation)
                     ?? types.Where(Mortal).OrderByDescending(Count).FirstOrDefault()
                     ?? types.OrderByDescending(Count).FirstOrDefault();
        if (chosen == null)
            return TimeSeries.Empty;
        if (Mortal(chosen))
            return WorldSeries.LivingOverTime(_db, chosen);
        return WorldSeries.EntitiesOfType(_db, chosen) is { } s ? s with { Label = $"{chosen.Name} ever" } : TimeSeries.Empty;
    }

    // Every entity of a period type (@period, or start_year/end_year on a type with no references), in
    // order. An end of 0 is the open, present era.
    private ChronicleEra[] Eras()
    {
        var now = Math.Max(0, _db.Ctx.Year);
        var eras = new List<ChronicleEra>();
        foreach (var type in _db.Types.Where(t => t.IsPeriod))
        {
            var start = type.Role(EntityRole.PeriodStart);
            var end = type.Role(EntityRole.PeriodEnd);
            foreach (var e in _db.Entities.Where(e => e.Type == type.Id))
            {
                var s = e.TryGetProperty(start, out var sv) ? sv.IntValue : 0;
                var f = e.TryGetProperty(end, out var ev) ? ev.IntValue : 0;
                eras.Add(new ChronicleEra(e.Id.Id, NameOf(e), s, f == 0 ? now : f, f == 0));
            }
        }

        return eras.OrderBy(e => e.Start).ThenBy(e => e.Id).ToArray();
    }

    /// <summary>
    /// Why a record happened: the rule that wrote it, then the rule that one ran inside, and so on back to
    /// the event the schedule started. A <c>schedule(...)</c> body's parent is the rule that scheduled it,
    /// years earlier, so a death can trace back to a birth. <paramref name="firing"/> is the record's own
    /// (<see cref="Database.Record.Firing"/>); an unknown firing, or 0, gives an empty chain.
    ///
    /// <para>A trigger's "because" is read from the changeset it was replaying -- its parent firing's
    /// -- which a closed changeset keeps a full copy of, so it says what changed as of then. Nothing here
    /// runs the simulation or depends on it having been instrumented beyond the firing log.</para>
    /// </summary>
    public Cause GetCause(int firing)
    {
        var steps = new List<CauseStep>();
        // Parents always have smaller serials, so this terminates; the guard is for a corrupt log.
        for (int serial = firing, guard = 0; serial != 0 && guard < 64; guard++)
        {
            if (!_db.TryGetFiring(serial, out var f))
                break;
            var kind = f.Rule.IsTrigger ? "trigger" : f.Rule.IsScheduled ? "scheduled" : f.Parent != 0 ? "call" : "event";
            var because = f.Rule.IsTrigger ? Because(f) : "";
            var records = _db.Records.Where(r => r.Firing == f.Serial).Select(r => r.Text).ToArray();
            steps.Add(new CauseStep(f.Serial, f.Rule.Name, kind, f.Rule.Line, Math.Max(f.Year, _db.StartYear),
                because, records));
            serial = f.Parent;
        }

        return new Cause(steps.ToArray());
    }

    // "<#12>Aldric</>: alive true -> false", or "<#12>Aldric</> was created": the change, in the changeset
    // the trigger replayed, that matched it.
    private string Because(Database.Firing f)
    {
        var who = _db.TryGetEntity(f.Cause, out var e)
            ? $"<{f.Cause}>{NameOf(e)}</>"
            : f.Cause.ToString();
        if (f.CauseCreated)
            return $"{who} was created";

        if (_db.History != null)
            foreach (var cs in _db.History.Changesets)
            {
                if (cs.Firing != f.Parent) continue;
                foreach (var change in cs.Changes)
                    if (change.Id.Id == f.Cause.Id)
                    {
                        var what = GetChangeDetails(change).Where(d => d.Label != "name")
                            .Select(d => $"{d.Label} {d.Value}");
                        return $"{who}: {string.Join(", ", what)}";
                    }
            }

        return $"{who} changed";
    }

    /// <summary>
    /// An entity's properties as they stood at the end of <paramref name="year"/>. Empty if it did not
    /// exist yet; its live details if <paramref name="year"/> is the present or later.
    ///
    /// <para>Nothing is snapshotted for this. A closed changeset holds a full copy of every entity it
    /// touched, so the last changeset on or before the year that touched this entity <i>is</i> its state
    /// then. The log is in time order, so the scan stops at the first changeset past the year.</para>
    ///
    /// <para>@display rows (Children, Members…) are left out of a past state: they are queries over the
    /// whole world, and answering them for another year would mean rebuilding every entity, not one.</para>
    /// </summary>
    public IList<EntityPropertyDisplay> GetEntityAt(uint eid, long year)
    {
        if (year >= _db.Ctx.Year || _db.History == null)
            return EntityPropertyDisplays(eid);

        Changeset.Changed? then = null;
        foreach (var cs in _db.History.Changesets)
        {
            if (cs.Year > year) break;
            foreach (var change in cs.Changes)
                if (change.Id.Id == eid)
                    then = change;
        }

        return then is { } c ? PropertyRows(c.New) : new List<EntityPropertyDisplay>();
    }

    /// <summary>
    /// Headline counts plus the always-meaningful series: narrative volume, world activity, and one
    /// cumulative entity count per type. All of it is replayed from the changeset log by
    /// <see cref="WorldSeries"/>, so the simulation pays nothing for it.
    /// </summary>
    public WorldOverview GetWorldOverview() => Memo(nameof(GetWorldOverview), null, ComputeWorldOverview);

    private WorldOverview ComputeWorldOverview()
    {
        if (_db.History == null)
            return new WorldOverview(0, 0, 0, 0, Array.Empty<TimeSeries>(), Array.Empty<ChartableProperty>());

        var series = new List<TimeSeries>
        {
            WorldSeries.RecordsPerYear(_db),
            WorldSeries.ChangesPerYear(_db),
        };
        series.AddRange(WorldSeries.EntitiesOfEveryType(_db));

        var properties = WorldSeries.Chartable(_db)
            .Select(c => new ChartableProperty((int)c.Type.Id.Id, c.Type.Name, c.Property.Name,
                c.IsBool ? "bool" : "number"))
            .ToArray();

        return new WorldOverview(Math.Max(0, _db.Ctx.Year), _db.Entities.Count(), _db.Records.Count,
            _db.History.Changesets.Count, series.ToArray(), properties);
    }

    /// <summary>One property's history, replayed from the changeset log. See <see cref="WorldSeries.PropertyOverTime"/>.</summary>
    public TimeSeries GetPropertySeries(int typeId, string propertyName) =>
        Memo(nameof(GetPropertySeries), (typeId, propertyName), () => _db.History == null
            ? TimeSeries.Empty
            : WorldSeries.PropertyOverTime(_db, _db.GetEntityType(new EntityTypeId((uint)typeId)), propertyName));

    // What Home and World show only changes when the world does, and every tab switch asks again: opening
    // World replayed the whole history each time (~750 ms of the browser's main thread at 700 years of
    // w.sg), and Home counted every record. Answers are kept until the world moves -- another world, any
    // property write, record, changeset or year -- and are immutable DTOs, so handing one out twice is safe.
    private (Database Db, long Writes, int Records, int Changesets, long Year) _memoWorld;
    private readonly Dictionary<(string Method, object? Arg), object> _memo = new();

    private T Memo<T>(string method, object? arg, Func<T> compute) where T : notnull
    {
        var world = (_db, _db.WriteVersion, _db.Records.Count, _db.History?.Changesets.Count ?? 0, _db.Ctx.Year);
        if (world != _memoWorld)
        {
            _memo.Clear();
            _memoWorld = world;
        }

        if (_memo.TryGetValue((method, arg), out var known))
            return (T)known;
        var answer = compute();
        _memo[(method, arg)] = answer;
        return answer;
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
                .Where(x => x.Id.Id == eid)
                .Select(x => new EntityChangeDisplay(x.Id, cs.Year, cs.ActionName, GetChangeDetails(x))))
            .ToList();
    }

    /// <summary>
    /// Everyone related to <paramref name="eid"/> by blood or marriage, within <paramref name="maxDepth"/>
    /// generations either way: ancestors, siblings (half-siblings too), every generation of descendants,
    /// and the partners and co-parents that make those families. It used to stop one generation down,
    /// which hid grandchildren, siblings and a child's spouse.
    ///
    /// <para>Every parent a node names is itself in the list: references to anyone outside the cut are
    /// cleared, so a view can follow P1/P2 without ever meeting a missing node.</para>
    /// </summary>
    public List<FamilyTreeNode> GetFamilyTree(uint eid, int maxDepth)
    {
        // The tree is built against the root entity's own type, not a hardcoded Person: any type
        // declaring parent1/parent2 gets a genealogy, and one that doesn't gets an empty list
        // instead of a tree of garbage read through the wrong type's property ids.
        if (!_db.TryGetEntity(new EntityId(eid), out var root))
            return new List<FamilyTreeNode>();
        var rootType = _db.GetEntityType(root.Type);
        var prop1 = rootType.Role(EntityRole.Parent1);
        var prop2 = rootType.Role(EntityRole.Parent2);
        if (!prop1.IsValid || !prop2.IsValid)
            return new List<FamilyTreeNode>();

        var life = new LifeFacts(this, rootType);
        uint Parent(Entity e, PropertyId p) => e.TryGetProperty(p, out var v) ? v.Id.Id : 0;

        // parent -> children, from one pass over the world. Walking down several generations with one
        // back-reference query per person would scan the whole type once per node.
        var childrenOf = new Dictionary<uint, List<uint>>();
        foreach (var e in _db.Entities)
        {
            if (e.Type.Id != rootType.Id.Id) continue;
            var p1 = Parent(e, prop1);
            var p2 = Parent(e, prop2);
            if (p1 != 0) Kids(p1).Add(e.Id.Id);
            if (p2 != 0 && p2 != p1) Kids(p2).Add(e.Id.Id);
        }
        List<uint> Kids(uint id) =>
            childrenOf.TryGetValue(id, out var l) ? l : childrenOf[id] = new List<uint>();
        IEnumerable<uint> ChildrenOf(uint id) =>
            childrenOf.TryGetValue(id, out var l) ? l : Enumerable.Empty<uint>();

        var nodes = new Dictionary<uint, FamilyTreeNode>();
        bool Add(uint id)
        {
            if (id == 0 || nodes.ContainsKey(id) || !_db.TryGetEntity(new EntityId(id), out var e))
                return false;
            nodes[id] = life.Node(e, Parent(e, prop1), Parent(e, prop2));
            return true;
        }

        // Up: ancestors.
        Add(eid);
        var up = new Queue<(uint id, int depth)>();
        up.Enqueue((eid, 0));
        while (up.TryDequeue(out var item))
        {
            if (item.depth >= maxDepth) continue;
            var n = nodes[item.id];
            foreach (var parent in new[] { n.P1, n.P2 })
                if (Add(parent))
                    up.Enqueue((parent, item.depth + 1));
        }

        // Across: siblings, whole and half.
        var me = nodes[eid];
        foreach (var parent in new[] { me.P1, me.P2 })
            if (parent != 0)
                foreach (var sibling in ChildrenOf(parent))
                    Add(sibling);

        // Down: every generation of descendants, each with its partner and every co-parent, which is what
        // turns a list of children into families.
        var down = new Queue<(uint id, int depth)>();
        down.Enqueue((eid, 0));
        while (down.TryDequeue(out var item))
        {
            Add(nodes[item.id].Partner);
            if (item.depth >= maxDepth) continue;
            foreach (var child in ChildrenOf(item.id))
            {
                Add(child);
                var c = nodes[child];
                Add(c.P1);
                Add(c.P2);
                down.Enqueue((child, item.depth + 1));
            }
        }

        // Keep the promise: no node names a parent the list does not contain.
        return nodes.Values
            .Select(n => n with
            {
                P1 = nodes.ContainsKey(n.P1) ? n.P1 : 0,
                P2 = nodes.ContainsKey(n.P2) ? n.P2 : 0,
            })
            .ToList();
    }

    public IList<EntityPropertyDisplay> GetEntityDetails(uint eid) => EntityPropertyDisplays(eid);

    // ---- helpers ----------------------------------------------------------

    // Participants are collected from the record's {$var} interpolation slots. Records emitted before a
    // rule bound any variable have none, so fall back to the entity marker the printer writes into the
    // text — the same fallback the records feed uses for its per-entity filter.
    private static bool MentionsEntity(Database.Record r, uint eid)
    {
        var participants = r.ParticipantSpan();
        if (participants.Length == 0)
            return r.Text.Contains($"<#{eid}>", StringComparison.Ordinal);
        foreach (var p in participants)
            if (p.Id == eid)
                return true;
        return false;
    }

    private static bool HasParents(EntityType type) => type.HasParents;

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

    /// <summary>
    /// Birth, death and partner for a family tree, read through the type's roles like its parents are:
    /// <c>@born</c>, <c>@died</c>, <c>@alive</c> or <c>@dead</c>, <c>@partner</c> -- or, by default,
    /// <c>birthdate</c>, <c>deathdate</c>, <c>alive</c> and <c>partner</c>. A type without them simply
    /// shows less.
    /// </summary>
    private sealed class LifeFacts(WorldSession s, EntityType type)
    {
        private readonly PropertyId _birth = type.Role(EntityRole.Birth);
        private readonly PropertyId _death = type.Role(EntityRole.Death);
        private readonly PropertyId _alive = type.Role(EntityRole.Alive);
        private readonly PropertyId _dead = type.Role(EntityRole.Dead);
        private readonly PropertyId _partner = type.Role(EntityRole.Partner);
        private Dictionary<uint, long>? _created;

        public FamilyTreeNode Node(Entity e, uint p1, uint p2)
        {
            var born = Number(e, _birth);
            if (born == 0) born = Created(e.Id.Id);
            var died = Number(e, _death);
            var dead = died != 0
                       || (_alive.IsValid && e.TryGetProperty(_alive, out var a) && !a.BoolValue)
                       || (_dead.IsValid && e.TryGetProperty(_dead, out var d) && d.BoolValue);
            var partner = _partner.IsValid && e.TryGetProperty(_partner, out var p) ? p.Id.Id : 0;
            return new FamilyTreeNode(e.Id.Id, NameOf(e), p1, p2)
            {
                Born = born, Died = died, Dead = dead, Partner = partner,
            };
        }

        private static long Number(Entity e, PropertyId id) =>
            id.IsValid && e.TryGetProperty(id, out var v) ? v.IntValue : 0;

        // The year the entity was created, from the first changeset that shows it with no previous self.
        // Built once per tree, on first need: most stories give a person a birthdate and never get here.
        private long Created(uint id)
        {
            if (_created == null)
            {
                _created = new();
                if (s._db.History != null)
                    foreach (var cs in s._db.History.Changesets)
                    foreach (var c in cs.Changes)
                        if (c.Created)
                            _created.TryAdd(c.Id.Id, cs.Year);
            }

            return _created.GetValueOrDefault(id);
        }
    }

    private IList<EntityChangeDisplay> GetChangesetDetails(Changeset cs) =>
        cs.Changes.Select(x => new EntityChangeDisplay(x.Id, cs.Year, cs.ActionName, GetChangeDetails(x)))
            .ToList();

    private IList<EntityPropertyDisplay> GetChangeDetails(Changeset.Changed c)
    {
        if (c.Created)
        {
            return c.New.Properties.Where(p => p.Id.IsValid)
                .Select(p => new EntityPropertyDisplay(_db.GetPropertyName(p.Id), PrintValue(p.Id, p.Value)))
                .ToList();
        }

        return c.Prev.Properties.Where(p => p.Id.IsValid)
            .Select(p =>
            {
                c.TryGetNext(p.Id, out var p1); // every property the change wrote is recorded
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

        var details = PropertyRows(e);
        var t = _db.GetEntityType(e.Type);
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

    // The Type row, then every set property. Shared by the live details and a past state.
    private List<EntityPropertyDisplay> PropertyRows(Entity e)
    {
        var rows = e.Properties.Where(p => p.Id.IsValid)
            .Select(p => new EntityPropertyDisplay(_db.GetPropertyName(p.Id), PrintValue(p.Id, p.Value)))
            .ToList();
        rows.Insert(0, new EntityPropertyDisplay("Type", _db.GetEntityType(e.Type).Name));
        return rows;
    }

    private static string NameOf(Entity e) =>
        e.TryGetProperty(Database.PropName, out var n) && n.Value is { } s ? s : e.Id.ToString();

    // The entities a record is about: its {$var} participants, or, for a record that bound none, the
    // <#id> links the printer wrote into its text. Same rule as MentionsEntity, for every id at once.
    private static IEnumerable<uint> Mentioned(Database.Record r)
    {
        if (r.Participants is { Length: > 0 })
        {
            foreach (var p in r.Participants)
                if (!p.IsNull) yield return p.Id;
            yield break;
        }

        // Scanned by hand rather than with a Regex: nothing else in the engine uses one, and the browser
        // build's trimmer drops System.Text.RegularExpressions (240 KB) only while that stays true.
        var text = r.Text;
        for (var i = text.IndexOf("<#", StringComparison.Ordinal); i >= 0;
             i = text.IndexOf("<#", i + 2, StringComparison.Ordinal))
        {
            uint id = 0;
            var j = i + 2;
            while (j < text.Length && char.IsAsciiDigit(text[j]))
                id = id * 10 + (uint)(text[j++] - '0');
            if (j > i + 2 && j < text.Length && text[j] == '>')
                yield return id;
        }
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
