using Moirai.Core;
using ExecutionContext = Moirai.Core.ExecutionContext;

public class And : IValue
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
    public PropertyValue Compute(ExecutionContext ctx)
    {
        throw new NotImplementedException();
    }
    public bool IsTrue(ExecutionContext ctx)
    {
        foreach (var p in Predicates)
        {
            if (!p.IsTrue(ctx))
                return false;
        }
        return true;
    }
    public (string where, string? joins) ToSql(ExecutionContext ctx)
    {
        var wheres = "";
        var joins = "";
        foreach (var predicate in Predicates)
        {
            var (w, j) = predicate.ToSql(ctx);
            if (wheres != "")
                wheres += " AND " + w;
            else
                wheres = w;
            if (j != null)
            {
                joins += " " + j;
            }
        }
        return (wheres, joins);
    }
}
