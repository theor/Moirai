using Moirai.Core;

/// <summary>
/// Backs the <c>add(coll, x)</c> / <c>remove(coll, x)</c> built-in functions: inserts/deletes a member
/// in an entity's collection property. Implemented as an <see cref="IValueCall"/> (like
/// <c>record</c>/<c>mark</c>); when used as a statement it is wrapped in a CallInstruction whose
/// Execute calls <see cref="Compute"/>. <see cref="Owner"/> is the path to the owning entity (the
/// collection path minus its final segment) and <see cref="Collection"/> is that final segment.
/// Writes go straight to the <c>collection</c> child table (set semantics via its primary key) and are
/// not recorded in the changeset, so triggers do not react to collection add/remove.
/// </summary>
public class CollectionMutate : IValueCall
{
    public readonly PropertyPath FullPath; // e.g. $c.parents — kept for round-trip printing
    public readonly PropertyPath Owner;
    public readonly PropertyId Collection;
    public readonly IValue Value;
    public readonly bool IsAdd;

    public CollectionMutate(PropertyPath fullPath, PropertyPath owner, PropertyId collection, IValue value, bool isAdd)
    {
        FullPath = fullPath;
        Owner = owner;
        Collection = collection;
        Value = value;
        IsAdd = isAdd;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var ownerId = Owner.Compute(ctx).Id;
        var valueId = Value.Compute(ctx).Id;
        if (IsAdd)
            ctx.Database.AddToCollection(ownerId, Collection, valueId);
        else
            ctx.Database.RemoveFromCollection(ownerId, Collection, valueId);
        return true; // keep the effect loop going (BoolValue true)
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return FullPath;
        yield return Value;
    }
}
