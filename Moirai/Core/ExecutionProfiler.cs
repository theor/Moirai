using System.Diagnostics;
using System.Text;

/// <summary>
/// Runtime-toggleable profiler for a single simulation run (one <c>PassYears</c> call).
///
/// Records, per scheduled event and per reactive trigger:
///   - <b>attempts</b>   events: number of invocations; triggers: number of times evaluated
///   - <b>successes</b>  events: ran to completion (returned true); triggers: predicate matched and effects ran
///   - <b>hit rate</b>   successes / attempts
///   - <b>self time</b>  wall time spent in that event/trigger, EXCLUDING nested measured scopes
///                       (an event calling another event via <c>call()</c>, or a trigger's effects calling
///                       events, and the triggers fired after an event are all measured separately).
///   - <b>inclusive time</b> wall time including those nested measured scopes.
///
/// Unlike <see cref="Profiler"/> (compile-time <c>[Conditional("DEBUG")]</c> property-hit counting), this is
/// a plain runtime object guarded only by a null check, so it profiles Release builds too. It is the
/// intended foundation for a deep optimization pass: self time tells you where cycles actually go, and the
/// trigger hit rate exposes how much trigger evaluation is wasted on non-matching changes.
/// </summary>
public sealed class ExecutionProfiler
{
    public sealed class Stat
    {
        public int Id;
        public string Name = "";
        public long Attempts;
        public long Successes;
        public long SelfTicks;
        public long InclusiveTicks;

        public double SelfMs => SelfTicks * 1000.0 / Stopwatch.Frequency;
        public double InclusiveMs => InclusiveTicks * 1000.0 / Stopwatch.Frequency;
        public double AvgSelfMicros => Attempts == 0 ? 0 : SelfTicks * 1_000_000.0 / Stopwatch.Frequency / Attempts;
        public double HitRate => Attempts == 0 ? 0 : (double)Successes / Attempts;
    }

    /// <summary>Number of simulated years this profile covers.</summary>
    public long Years;

    /// <summary>Total wall ticks of the run (the whole <c>PassYears</c> loop).</summary>
    public long ElapsedTicks;

    public double ElapsedMs => ElapsedTicks * 1000.0 / Stopwatch.Frequency;

    private readonly Dictionary<int, Stat> _events = new();
    private readonly Dictionary<int, Stat> _triggers = new();

    // Running accumulator used to derive self time under nesting. Whenever a measured scope completes it
    // folds its inclusive time into this counter; an enclosing scope subtracts the delta accrued during its
    // body to obtain its own self time. Works for arbitrary nesting and sibling scopes (single-threaded).
    private long _childTicks;

    public IReadOnlyCollection<Stat> Events => _events.Values;
    public IReadOnlyCollection<Stat> Triggers => _triggers.Values;

    /// <summary>Token captured when a measured scope begins; hand it back to <see cref="RecordEvent"/>/<see cref="RecordTrigger"/>.</summary>
    public readonly struct Scope
    {
        internal readonly long StartTimestamp;
        internal readonly long ChildTicksAtStart;

        internal Scope(long startTimestamp, long childTicksAtStart)
        {
            StartTimestamp = startTimestamp;
            ChildTicksAtStart = childTicksAtStart;
        }
    }

    public Scope Begin() => new(Stopwatch.GetTimestamp(), _childTicks);

    public void RecordEvent(EventTrigger e, in Scope scope, bool success) => Record(_events, e, scope, success);
    public void RecordTrigger(EventTrigger t, in Scope scope, bool success) => Record(_triggers, t, scope, success);

    private void Record(Dictionary<int, Stat> table, EventTrigger et, in Scope scope, bool success)
    {
        long inclusive = Stopwatch.GetTimestamp() - scope.StartTimestamp;
        long self = inclusive - (_childTicks - scope.ChildTicksAtStart);
        _childTicks = scope.ChildTicksAtStart + inclusive;

        if (!table.TryGetValue(et.Id, out var s))
            table[et.Id] = s = new Stat { Id = et.Id, Name = et.Name };
        s.Attempts++;
        if (success)
            s.Successes++;
        s.SelfTicks += self;
        s.InclusiveTicks += inclusive;
    }

    public string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"=== Execution profile: {Years} years in {ElapsedMs:F1} ms ===");

        AppendTable(sb, "Events (executed)", "exec", _events.Values);
        AppendTable(sb, "Triggers (attempted)", "attempts", _triggers.Values);

        double eventSelf = _events.Values.Sum(s => s.SelfMs);
        double trigSelf = _triggers.Values.Sum(s => s.SelfMs);
        double covered = eventSelf + trigSelf;
        sb.AppendLine();
        sb.AppendLine(
            $"Coverage: events {eventSelf:F1} ms + triggers {trigSelf:F1} ms = {covered:F1} ms of {ElapsedMs:F1} ms " +
            $"({(ElapsedMs <= 0 ? 0 : 100 * covered / ElapsedMs):F1}% self; remainder is scheduling/query overhead)");
        return sb.ToString();
    }

    private static void AppendTable(StringBuilder sb, string title, string countHeader, IEnumerable<Stat> stats)
    {
        var rows = stats.OrderByDescending(s => s.SelfTicks).ToList();
        sb.AppendLine();
        sb.AppendLine($"{title}:");
        sb.AppendLine(
            $"  {"name",-28} {countHeader,9} {"ok",9} {"hit%",6} {"self ms",10} {"incl ms",10} {"avg us",9}");

        long attempts = 0, ok = 0;
        double self = 0, incl = 0;
        foreach (var s in rows)
        {
            sb.AppendLine(
                $"  {Trunc(s.Name, 28),-28} {s.Attempts,9} {s.Successes,9} {s.HitRate * 100,5:F1}% " +
                $"{s.SelfMs,10:F2} {s.InclusiveMs,10:F2} {s.AvgSelfMicros,9:F1}");
            attempts += s.Attempts;
            ok += s.Successes;
            self += s.SelfMs;
            incl += s.InclusiveMs;
        }

        double hit = attempts == 0 ? 0 : 100.0 * ok / attempts;
        sb.AppendLine(
            $"  {"TOTAL",-28} {attempts,9} {ok,9} {hit,5:F1}% {self,10:F2} {incl,10:F2}");
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n);
}
