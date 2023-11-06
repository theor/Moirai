using Pcg.Core;

public interface IValue
{
    PropertyValue Compute(PredicateContext ctx);
    bool IsTrue(PredicateContext ctx) => Compute(ctx).BoolValue;
}
public interface IFilter{
    PropertyValue Compute(PredicateContext ctx);}
public class FilterAtStart : IFilter
{
    // checked separately
    public PropertyValue Compute(PredicateContext ctx) => false;
}

public class FilterExactlyXEveryYYears(int count, int years) : IFilter
{
    public readonly int Count = count;
    public readonly int Years = years;
    // TODO only works for "1 every 1 year..."
    public PropertyValue Compute(PredicateContext ctx) => true;
}
public class FilterProbabilityXPerYears : IFilter
{
    public RandomEvent Event;
    public FilterProbabilityXPerYears(int occurences, int expectedInterval)
    {
        Event = new RandomEvent(occurences, expectedInterval);
    }
    public PropertyValue Compute(PredicateContext ctx) => Event.Sample(ctx.Rnd);
}