using System.Linq;
using Moirai.Core;

/// <summary>
/// Backs the <c>contains(coll, x)</c> and <c>count(coll)</c> built-in functions over a collection
/// property. Compiles to a correlated subquery against the <c>collection</c> child table for
/// pick/each predicates (<see cref="ToSql"/>), and evaluates in-memory for trigger <c>when</c>
/// predicates and other non-SQL contexts (<see cref="Compute"/>). <see cref="FullPath"/> (e.g.
/// <c>$c.parents</c>) is kept for round-trip printing; <see cref="Owner"/>/<see cref="Collection"/>
/// are its split form used for evaluation.
/// </summary>
public class CollectionQuery : IValueCall, IValueSql
{
    public enum QueryKind { Contains, Count }

    public readonly QueryKind Kind;
    public readonly PropertyPath FullPath;
    public readonly PropertyPath Owner;
    public readonly PropertyId Collection;
    public readonly IValue? Value; // member to test, for Contains only

    public CollectionQuery(QueryKind kind, PropertyPath fullPath, PropertyPath owner, PropertyId collection, IValue? value)
    {
        Kind = kind;
        FullPath = fullPath;
        Owner = owner;
        Collection = collection;
        Value = value;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var ownerId = Owner.Compute(ctx).Id;
        if (Kind == QueryKind.Count)
            return ctx.Database.CollectionCount(ownerId, Collection);
        return ctx.Database.CollectionContains(ownerId, Collection, Value!.Compute(ctx).Id);
    }

    // An owner/value that is the current query variable compiles to the bare "default__id"; inside the
    // correlated subquery that must be qualified to the outer entity row, "entity.default__id".
    private static string Qualify(string s) => s == "default__id" ? "entity.default__id" : s;

    public (string where, string? joins) ToSql(ExecuteContext ctx)
    {
        var (ownerSql, ownerJoins) = ((IValueSql)Owner).ToSql(ctx);
        ownerSql = Qualify(ownerSql);
        long propKey = Database.CollPropKey(Collection);

        if (Kind == QueryKind.Count)
            return ($"(SELECT COUNT(*) FROM collection WHERE owner = {ownerSql} AND prop = {propKey})",
                ownerJoins);

        var (valSql, valJoins) = ((IValueSql)Value!).ToSql(ctx);
        valSql = Qualify(valSql);
        var joins = string.Concat(new[] { ownerJoins, valJoins }.Where(s => !string.IsNullOrEmpty(s)));
        var where =
            $"EXISTS (SELECT 1 FROM collection WHERE owner = {ownerSql} AND prop = {propKey} AND value = {valSql})";
        return (where, joins.Length == 0 ? null : joins);
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return FullPath;
        if (Value != null)
            yield return Value;
    }
}
