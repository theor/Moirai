using Moirai.Core;

public interface IFilter{
    int Compute(PredicateContext ctx, long currentYear);
}
public class FilterAtStart : IFilter
{
    // checked separately
    public int Compute(PredicateContext ctx, long currentYear) => 0;
}

public class FilterExactlyXEveryYYears : IFilter
{
    public readonly int Count;
    public readonly int Years;
    public readonly int EventId;

    private long _lastYearPlanned;

    private List<(long year, int count)> _planning = new();

    public FilterExactlyXEveryYYears(int count, int years, int eventId)
    {
        Count = count;
        Years = years;
        EventId = eventId;
        if (Years < 1 || Count < 1)
            throw new InvalidDataException("Years < 1");
    }

    // TODO only works for "1 every 1 year..."
    public int Compute(PredicateContext ctx, long currentYear)
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
            var prevYear = currentYear;
            for (int i = 0; i < Count; i++)
            {
                var y = prevYear + ctx.Rnd.GenerateNext((uint)((uint)(Years)/(Math.Max(1, Count - i - 1))));
                prevYear = y;
                _planning.Add((Math.Min(y, currentYear + Years - 1), 1 ));
            }
        }

        int count = 0;
        while (_planning.Any() && _planning.FirstOrDefault().year == currentYear)
        {
            var p = _planning.First();
            _planning.RemoveAt(0);
            count++;
            Console.WriteLine(p);
        }
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
    public int Compute(PredicateContext ctx, long currentYear) => Event.Sample(ctx.Rnd);
}
