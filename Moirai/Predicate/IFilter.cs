using Moirai.Core;

public interface IFilter{
    int Compute(ExecuteContext ctx, long currentYear);
}
public class FilterAtStart : IFilter
{
    // checked separately
    public int Compute(ExecuteContext ctx, long currentYear) => 0;
}

public class FilterExactlyXEveryYYears : IFilter
{
    public readonly int Count;
    public readonly int Years;
    public readonly int EventId;

    // Before any year, so the first one is planned even when it is year 0.
    private long _lastYearPlanned = long.MinValue;

    // The years this window's occurrences fall on, ascending.
    private readonly List<long> _planning = new();

    public FilterExactlyXEveryYYears(int count, int years, int eventId)
    {
        Count = count;
        Years = years;
        EventId = eventId;
        if (Years < 1 || Count < 1)
            throw new InvalidDataException("Years < 1");
    }

    // Exactly Count occurrences in each window of Years years, starting with the first year it is asked
    // about. Each one lands on a year of the window drawn uniformly, independently of the others (so two
    // can share a year); for Count = 1 that is the one draw the schedule has always made.
    public int Compute(ExecuteContext ctx, long currentYear)
    {
        if (Years == 1)
        {
            if (currentYear > _lastYearPlanned)
            {
                _lastYearPlanned = currentYear;
                return Count;
            }

            return 0;
        }

        if (_lastYearPlanned <= currentYear)
        {
            _lastYearPlanned = currentYear + Years;
            for (int i = 0; i < Count; i++)
                _planning.Add(currentYear + ctx.Rnd.GenerateNext((uint)Years));
            _planning.Sort();
        }

        // Everything due by now: a year the schedule was not asked about must not strand its occurrences.
        int count = 0;
        while (count < _planning.Count && _planning[count] <= currentYear)
            count++;
        _planning.RemoveRange(0, count);
        return count;
    }
}
public class FilterProbabilityXPerYears : IFilter
{
    public RandomEvent Event;
    public FilterProbabilityXPerYears(int occurences, int expectedInterval)
    {
        Event = new RandomEvent(occurences, expectedInterval);
    }
    public int Compute(ExecuteContext ctx, long currentYear) => Event.Sample(ctx.Rnd);
}
