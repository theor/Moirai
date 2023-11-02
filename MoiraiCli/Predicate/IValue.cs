public interface IValue
{
    PropertyValue Compute(PredicateContext ctx);
    bool IsTrue(PredicateContext ctx) => Compute(ctx).BoolValue;
}