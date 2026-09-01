using Moirai.Core;

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
    string[] Tags);

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
