using Moirai.Core;
using Moirai.Parser;

namespace Moirai.Api;

/// <summary>
/// The wire contract between a Moirai host and its viewer, shared by every transport. These types were
/// nested inside the SignalR hub; they live here so the WebAssembly host can hand the same shapes to the
/// same client. Names and member order are the contract — the Svelte client's TypeScript mirrors them by
/// hand (<c>ClientAppSvelte/src/lib/types.ts</c>), so renaming a member here breaks the UI silently.
/// </summary>
public record EntityPropertyDisplay(string Label, string Value);

public record ActionData(int Id, string Name);

public record TypeData(int Id, string Name);

/// <summary>What a viewer needs to know once, at startup, to render the rest.</summary>
public struct ClientData
{
    public ActionData[] Actions;
    public TypeData[] Types;
    public ulong Seed;
}

/// <summary>
/// One moment in an entity's life: either a record it appears in (the narrative) or a changeset that
/// touched it (the ledger). <c>ChangesetId</c> orders the two against each other — a record carries
/// the id of the changeset that produced it, so sorting by it interleaves narrative and ledger in
/// causal order rather than lumping all of one year's records before all of its changes.
/// </summary>
public record BiographyEntry(
    long Year,
    int ChangesetId,
    string Kind,
    string Text,
    string ActionName,
    IList<EntityPropertyDisplay> Changes,
    string[] Tags,
    int Firing = 0);

/// <summary>
/// One link in a record's chain of causes: a rule that ran. <c>Kind</c> is <c>event</c> (scheduled),
/// <c>call</c> (an event another rule call()ed) or <c>trigger</c>. <c>Because</c> says what set a
/// trigger off -- which entity changed, and how -- in the same entity-link markup record text uses, and
/// is empty for an event, which the schedule ran. <c>Line</c> is the rule's 1-based line in the story.
/// <c>Records</c> are what that rule wrote, so each step reads as something that happened.
/// </summary>
public record CauseStep(int Firing, string Rule, string Kind, int Line, long Year, string Because, string[] Records);

/// <summary>From the rule that wrote a record back to the one the schedule started, nearest first.</summary>
public record Cause(CauseStep[] Steps);

public record Biography(
    uint Id,
    string Name,
    string TypeName,
    bool HasFamily,
    IList<EntityPropertyDisplay> Details,
    BiographyEntry[] Timeline);

/// <summary>A (type, property) pair the dashboard can plot: bools as a count of true, numbers as a mean.</summary>
public record ChartableProperty(int TypeId, string TypeName, string PropertyName, string Kind);

public record WorldOverview(
    long Year,
    int Entities,
    int Records,
    int Changesets,
    TimeSeries[] Series,
    ChartableProperty[] Properties);

/// <summary>
/// One row of the rule-coverage report: how often an event or trigger has fired over the whole life
/// of the current world. <c>Attempts</c>/<c>Successes</c> are the engine's always-on counters
/// (<see cref="EventTrigger.Attempts"/>), not the per-run profiler's.
/// </summary>
public record RuleCoverage(
    int Id,
    string Name,
    string Kind,
    string Schedule,
    long Attempts,
    long Successes,
    string[] Tags);

public record RuleCoverageReport(long Year, RuleCoverage[] Rules);

public record EntityChangeDisplay(EntityId Id, long Year, string ActionName, IList<EntityPropertyDisplay> Changes);

/// <summary>
/// A node in an entity's genealogy. Equality is the id alone, so a <see cref="HashSet{T}"/> of these
/// dedupes by entity while letting the first-seen name win — which is what lets the ancestor sweep and
/// the co-parent fill-in run over the same set without overwriting each other.
/// </summary>
public struct FamilyTreeNode(uint id, string name, uint p1, uint p2) : IEquatable<FamilyTreeNode>
{
    public uint Id { get; init; } = id;
    public string Name { get; init; } = name;
    public uint P1 { get; init; } = p1;
    public uint P2 { get; init; } = p2;

    /// <summary>
    /// Year of birth: the type's <c>birthdate</c> if it declares one and it is set, else the year the
    /// entity was created. 0 when neither is known.
    /// </summary>
    public long Born { get; init; }

    /// <summary>Year of death from the type's <c>deathdate</c>, or 0 when unknown or still alive.</summary>
    public long Died { get; init; }

    /// <summary>
    /// Dead, from <c>deathdate</c> being set or a bool <c>alive</c> being false. Separate from
    /// <see cref="Died"/> because a story can kill someone without recording when.
    /// </summary>
    public bool Dead { get; init; }

    /// <summary>The type's <c>partner</c> reference, or 0.</summary>
    public uint Partner { get; init; }

    public void Deconstruct(out uint id, out string name, out uint p1, out uint p2)
    {
        id = this.Id;
        name = this.Name;
        p1 = this.P1;
        p2 = this.P2;
    }

    public bool Equals(FamilyTreeNode other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is FamilyTreeNode other && Equals(other);
    public override int GetHashCode() => (int)Id;
    public static bool operator ==(FamilyTreeNode left, FamilyTreeNode right) => left.Equals(right);
    public static bool operator !=(FamilyTreeNode left, FamilyTreeNode right) => !left.Equals(right);
}

/// <summary>One entity and how much the story has had to say about it.</summary>
public record NotableEntity(uint Id, string Name, int Mentions, long FirstYear, long LastYear);

/// <summary>
/// The most mentioned entities of one type. Groups come most-mentioned type first. <c>HasFamily</c> says
/// the type declares parent1/parent2, i.e. its entities have a family tree to show.
/// </summary>
public record NotableGroup(int TypeId, string TypeName, bool HasFamily, NotableEntity[] Top);

/// <summary>
/// A named period of the world's history: an entity whose type declares number <c>start_year</c> and
/// <c>end_year</c> (w.sg's <c>Era</c>). <c>End</c> is the present year for the open period, whose
/// <c>end_year</c> is still 0.
/// </summary>
public record ChronicleEra(uint Id, string Name, long Start, long End, bool Open);

/// <summary>A record chosen for the chronicle because of its weight (see <see cref="Database.Record.Weight"/>).</summary>
public record ChronicleEntry(long Year, int ChangesetId, string Text, int Weight, string[] Tags);

/// <summary>How many records carry a tag: what the world has mostly been about.</summary>
public record ChronicleTag(string Tag, int Records);

/// <summary>
/// The whole world on one card: its span, how its population moved, its eras and its turning points.
/// <c>Weighted</c> says whether the story weighs its records at all; when it does not, the turning
/// points are only an even sample across time, and the viewer can say so.
/// </summary>
public record Chronicle(
    long StartYear,
    long Year,
    int Records,
    TimeSeries Population,
    ChronicleEra[] Eras,
    ChronicleEntry[] TurningPoints,
    ChronicleTag[] Tags,
    bool Weighted);

public struct Result
{
    public EntityId Eid;
    public IList<EntityPropertyDisplay> Properties;
}

public struct QueryResult
{
    public string? Sql;
    public Result[] Results;
    public string[] Errors;
    public string Query;
}

/// <summary>
/// One item of the record feed a viewer subscribes to. The type discriminates: <c>Record</c> carries a
/// new narrative record, <c>Year</c> is the heartbeat that advances the clock, and <c>Reset</c> tells the
/// viewer to clear itself (carrying, on a hot reload, the year the world was at so the UI can say where
/// it was).
/// </summary>
public struct Message(Database.Record? record)
{
    public enum MessageType
    {
        Reset,
        Record,
        Year
    }

    public MessageType Type = MessageType.Record;
    public Database.Record? Record = record;
    public long Year;

    public static Message Reset(long? targetYears) =>
        new() { Type = MessageType.Reset, Year = targetYears.GetValueOrDefault(0) };

    public static Message YearMessage(long year) => new() { Type = MessageType.Year, Year = year };
}

/// <summary>
/// One tick of the record feed as a host hands it to a viewer: the messages, and the cursor to pass back
/// next time. See <see cref="WorldSession.DrainFeed"/>.
/// </summary>
public record FeedTick(int Cursor, List<Message> Messages);

/// <summary>
/// One parser diagnostic, positioned in the story text.
///
/// <para>The coordinates are <see cref="StoryParser.Error"/>'s own and are deliberately unconverted:
/// <b>Line is 1-based, Col is 0-based</b> (the ANTLR token convention the engine inherited — see the
/// comment on <c>Error</c>'s span constructor). A viewer converts once, somewhere it can test.</para>
/// </summary>
public record StoryDiagnostic(
    string Severity,
    string Code,
    int Line,
    int Col,
    int LineEnd,
    int ColEnd,
    string Message);

/// <summary>
/// What came of handing a new story to a session. <c>Applied</c> is false when the story did not parse,
/// in which case the world is exactly as it was and <c>Year</c> is where it still stands.
/// </summary>
public record StoryApplyResult(bool Applied, long Year, StoryDiagnostic[] Diagnostics);
