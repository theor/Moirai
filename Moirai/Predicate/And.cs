using Moirai.Core;

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
    public (string where, string? joins) ToSql(ExecuteContext ctx)
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
