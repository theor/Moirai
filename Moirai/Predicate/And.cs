using Moirai.Core;

public class And : IValueSql
{
    public List<IValue> Predicates = new();
    public And(params IValue[] predicates)
    {
        Predicates.AddRange(predicates);
    }
    public And(List<IValue> predicates)
    {
        Predicates.AddRange(predicates);
    }
    public PropertyValue Compute(ExecuteContext ctx)
    {
        throw new NotImplementedException();
    }
    public bool IsTrue(ExecuteContext ctx)
    {
        foreach (var p in Predicates)
        {
            if (!p.IsTrue(ctx))
                return false;
        }
        return true;
    }
}
