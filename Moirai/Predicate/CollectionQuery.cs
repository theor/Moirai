using System.Linq;
using Moirai.Core;

/// <summary>
/// Backs the <c>contains(coll, x)</c> and <c>count(coll)</c> built-in functions over a collection
/// property, evaluated in-memory via <see cref="Compute"/>. <see cref="FullPath"/> (e.g.
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

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return FullPath;
        if (Value != null)
            yield return Value;
    }
}
