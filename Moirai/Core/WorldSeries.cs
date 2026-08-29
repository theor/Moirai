namespace Moirai.Core;

/// <summary>A labelled series of samples over simulated years, ready to plot.</summary>
public sealed record TimeSeries(string Label, long[] Years, double[] Values)
{
    public static readonly TimeSeries Empty = new("", Array.Empty<long>(), Array.Empty<double>());
}

/// <summary>
/// Time series derived from <see cref="History"/> after the fact.
///
/// The simulation records nothing about how the world changed over time beyond the changeset log — but
/// a closed changeset carries a full clone of every entity it touched (see <c>Changeset.CloseChangeset</c>),
/// so replaying the log reconstructs any property's history exactly. That is what makes "population over
/// time" or "average prosperity over time" answerable without the engine having tracked either, and
/// without paying for the bookkeeping on every run.
/// </summary>
public static class WorldSeries
{
    /// <summary>Enough points to read a shape without shipping one sample per year of a ten-thousand-year run.</summary>
    public const int DefaultMaxPoints = 400;

    // id/type/name and the leading placeholder: every type carries these, and none of them is a
    // property of the world worth plotting.
    private static readonly int BuiltinPropertyCount = Database.DefaultProperties().Count;

    /// <summary>The story's own types, skipping the engine's built-ins (<c>Time</c> and the placeholder).</summary>
    public static IEnumerable<EntityType> StoryTypes(Database db) =>
        db.Types.Skip(db.BuiltinTypes).OrderBy(t => t.Name);

    /// <summary>
    /// How a series downsamples. A <see cref="Rate"/> is averaged over its bucket, so the axis still
    /// reads "per year" at any zoom; a <see cref="Level"/> takes the bucket's last sample, so it reads
    /// the state at that year rather than a smear of the years around it.
    /// </summary>
    public enum Kind
    {
        Level,
        Rate,
    }

    public static TimeSeries RecordsPerYear(Database db, int maxPoints = DefaultMaxPoints)
    {
        var domain = Domain(db);
        return Bucket("Records per year", Kind.Rate, Histogram(domain, db.Records.Select(r => r.Year)),
            domain, maxPoints);
    }

    public static TimeSeries ChangesPerYear(Database db, int maxPoints = DefaultMaxPoints)
    {
        var domain = Domain(db);
        var years = db.History?.Changesets.Select(c => c.Year) ?? Enumerable.Empty<long>();
        return Bucket("World changes per year", Kind.Rate, Histogram(domain, years), domain, maxPoints);
    }

    /// <summary>
    /// How many entities of <paramref name="type"/> had been created by each year. Entities are never
    /// deleted, so this is "how many have ever existed"; for "how many are alive now", plot the type's
    /// own bool property with <see cref="PropertyOverTime"/>. Null when the type never appears.
    /// </summary>
    public static TimeSeries? EntitiesOfType(Database db, EntityType type, int maxPoints = DefaultMaxPoints)
    {
        var domain = Domain(db);
        var creations = db.History?.Changesets.SelectMany(cs => cs.Changes
                            .Where(ch => ch.Prev.Id.IsNull && ch.New.Type == type.Id)
                            .Select(_ => cs.Year))
                        ?? Enumerable.Empty<long>();
        var perYear = Histogram(domain, creations);
        double running = 0;
        for (int i = 0; i < perYear.Length; i++)
            perYear[i] = running += perYear[i];
        return running == 0 ? null : Bucket(type.Name, Kind.Level, perYear, domain, maxPoints);
    }

    /// <summary>
    /// One property's history: for a bool, the number of entities holding it true at each year; for a
    /// number, their mean. Entities that have never been written are absent from the sample, so an
    /// average is over the entities that have a value, not over the whole type.
    /// </summary>
    public static TimeSeries PropertyOverTime(Database db, EntityType type, string propertyName,
        int maxPoints = DefaultMaxPoints)
    {
        var prop = type.Properties.Skip(BuiltinPropertyCount).FirstOrDefault(p => p.Name == propertyName);
        if (prop.Name == null || db.History == null)
            return TimeSeries.Empty;

        bool isBool = prop.Type.BaseType == PropertyValue.ValueBaseType.Bool;
        var pid = prop.PropertyId;
        var domain = Domain(db);
        var perYear = new double[domain.End - domain.Start + 1];

        // Running sum and set, so each year's level costs O(1) instead of a scan of everything tracked.
        var latest = new Dictionary<uint, double>();
        double sum = 0;
        long cursor = 0;
        double Level() => isBool ? sum : latest.Count == 0 ? 0 : sum / latest.Count;

        foreach (var cs in db.History.Changesets)
        {
            long year = Math.Clamp(cs.Year, domain.Start, domain.End) - domain.Start;
            while (cursor < year)
                perYear[cursor++] = Level();
            foreach (var ch in cs.Changes)
            {
                if (ch.New.Type != type.Id || !ch.New.TryGetProperty(pid, out var v))
                    continue;
                double value = isBool ? (v.BoolValue ? 1 : 0) : v.FloatValue;
                sum += latest.TryGetValue(ch.New.Id.Id, out var previous) ? value - previous : value;
                latest[ch.New.Id.Id] = value;
            }
        }

        while (cursor < perYear.Length)
            perYear[cursor++] = Level();

        var label = isBool
            ? $"{type.Name}.{prop.Name} — entities where true"
            : $"{type.Name}.{prop.Name} — mean";
        return Bucket(label, Kind.Level, perYear, domain, maxPoints);
    }

    /// <summary>
    /// The (type, property) pairs worth plotting. Refs, strings and enums are excluded: neither a mean
    /// nor a count-of-true says anything true about them.
    /// </summary>
    public static IEnumerable<(EntityType Type, PropertyDefinition Property, bool IsBool)> Chartable(Database db)
    {
        foreach (var type in StoryTypes(db))
        foreach (var prop in type.Properties.Skip(BuiltinPropertyCount))
        {
            if (prop.IsCollection)
                continue;
            switch (prop.Type.BaseType)
            {
                case PropertyValue.ValueBaseType.Bool:
                    yield return (type, prop, true);
                    break;
                case PropertyValue.ValueBaseType.Number:
                case PropertyValue.ValueBaseType.Float:
                case PropertyValue.ValueBaseType.Percentage:
                    yield return (type, prop, false);
                    break;
            }
        }
    }

    // The timeline runs from the year the world began to the year it has reached. Stories rarely start
    // at zero -- w.sg starts at 764 -- so indexing from zero would pad every chart with centuries of
    // flat nothing. Setup effects run inside Init() before Time exists, so their changesets carry year 0
    // and are clamped up into the first year, where they belong.
    private static (long Start, long End) Domain(Database db)
    {
        long start = Math.Max(0, db.StartYear);
        return (start, Math.Max(start, db.Ctx.Year));
    }

    private static double[] Histogram((long Start, long End) domain, IEnumerable<long> years)
    {
        var counts = new double[domain.End - domain.Start + 1];
        foreach (var y in years)
            counts[Math.Clamp(y, domain.Start, domain.End) - domain.Start]++;
        return counts;
    }

    private static TimeSeries Bucket(string label, Kind kind, double[] perYear,
        (long Start, long End) domain, int maxPoints)
    {
        int n = perYear.Length;
        int buckets = Math.Clamp(maxPoints, 1, n);
        var years = new long[buckets];
        var values = new double[buckets];
        for (int b = 0; b < buckets; b++)
        {
            int lo = (int)((long)b * n / buckets);
            int hi = (int)((long)(b + 1) * n / buckets);
            if (hi <= lo)
                hi = lo + 1;
            years[b] = domain.Start + Math.Min(hi - 1, domain.End - domain.Start);
            if (kind == Kind.Rate)
            {
                double total = 0;
                for (int i = lo; i < hi && i < n; i++)
                    total += perYear[i];
                values[b] = total / (hi - lo);
            }
            else
            {
                values[b] = perYear[Math.Min(hi - 1, n - 1)];
            }
        }

        return new TimeSeries(label, years, values);
    }
}
