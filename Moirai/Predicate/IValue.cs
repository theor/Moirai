using Moirai.Core;

public interface IValue
{
    PropertyValue Compute(PredicateContext ctx);
    bool HasTypeFilter(out EntityTypeId type);
    bool IsTrue(PredicateContext ctx) => Compute(ctx).BoolValue;
    string ToSql(PredicateContext ctx);
}

public class MatchAnyValue : IValue
{
    private MatchAnyValue(){}
    public static MatchAnyValue Instance = new();
    public PropertyValue Compute(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }

    public bool HasTypeFilter(out EntityTypeId type)
    {
        throw new NotImplementedException();
    }

    public string ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }
}
public class IsOfType : IValue
{
    public readonly IValue Entity;
    public readonly EntityTypeId ValueTypeId;
    public IsOfType(IValue entity, EntityTypeId valueTypeId)
    {
        Entity = entity;
        ValueTypeId = valueTypeId;
    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        var typeId = Entity.Compute(ctx).TypeId;
        var result = typeId == ValueTypeId;
        Profiler.HitOfType(typeId, result);
        return result;
    }
    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = ValueTypeId;
        return true;
    }
    public string ToSql(PredicateContext ctx) => $"type = " + ValueTypeId.Id;
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
