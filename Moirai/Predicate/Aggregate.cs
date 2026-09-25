using Moirai.Core;

/// `count T $v: (pred)`, and `sum`/`avg`/`min`/`max T $v: (pred, value)`: one number out of every entity
/// of T the predicate matches -- how many there are, or what their `value` adds up to, averages, or ranges
/// between. The predicate is a query like a pick's (narrowed the same way), the value is read with $v bound
/// to each match. Over no matches every kind gives 0, so tell "none" from "zero" with count.
///
/// It draws no random numbers of its own -- only a value expression that does would -- so adding one to a
/// rule moves nothing but what reads it.
public sealed class Aggregate : IValueCall, IValueSql
{
    public enum AggregateKind { Count, Sum, Avg, Min, Max }

    public readonly AggregateKind Kind;
    public readonly EntityTypeId EntityType;
    public readonly int VariableIndex;
    public readonly IValueSql? Predicate;
    public readonly IValue? Value;
    /// What the result is: a number for count, and for the others the value's own type -- except that a
    /// sum of percentages is a float (it runs past 100) and an average of whole numbers is one too.
    public readonly PropertyValue.ValueType ResultType;

    public Aggregate(AggregateKind kind, EntityTypeId entityType, int variableIndex, IValueSql? predicate,
        IValue? value, PropertyValue.ValueType resultType)
    {
        Kind = kind;
        EntityType = entityType;
        VariableIndex = variableIndex;
        Predicate = predicate;
        Value = value;
        ResultType = resultType;
    }

    public PropertyValue Compute(ExecuteContext ctx) => ctx.Database.Aggregate(this);

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    (int, PropertyValue.ValueType)? IValueCall.VariableIndex => (VariableIndex, PropertyValue.TypeTypedRef(EntityType));

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        if (Predicate != null)
            yield return Predicate;
        if (Value != null)
            yield return Value;
    }
}
