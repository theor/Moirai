using Pcg.Core;

public struct ComputedValue
{
    public enum ComputedValueType
    {
        Value,
        Path,
    }

    public readonly ComputedValueType Type;
    public readonly PropertyValue Value;
    public readonly PropertyPath Path;
    public ComputedValue(PropertyValue value)
    {
        Type = ComputedValueType.Value;
        Value = value;
        Path = default;
    }
    public ComputedValue(PropertyPath path)
    {
        Type = ComputedValueType.Path;
        Value = default;
        Path = path;
    }
    public static explicit operator ComputedValue(PropertyValue value) => new ComputedValue(value);

}

public enum CallType
{
    None,
    Pick,
    Each,
    When
}
public struct AssignPick : IEffect
{
    public readonly int VariableIndex;
    public readonly IPredicate Predicate;
    public readonly CallType CallType;
    public AssignPick(int variableIndex, IPredicate value, CallType callType)
    {
        VariableIndex = variableIndex;
        Predicate = value;
        CallType = callType;
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        switch (CallType)
        {

            case CallType.Pick:
                bool res = ctx.PickRandom(Predicate, out var val);
                ctx.SetArgument(VariableIndex, val);
                return res;
            case CallType.Each:
                throw new System.NotImplementedException();
                List<EntityId> results = new();
                ctx.FindAll(Predicate, ref results);
                break;
            default:
                throw new ArgumentOutOfRangeException(CallType.ToString());
        }
    }
}

// public struct PredicateParameter : IEffect
// {
//     public enum PredicateParameterType
//     {
//         Value,
//         Predicate,
//         Argument,
//     }
//
//     public readonly PredicateParameterType Type;
//     public readonly IPredicate? Predicate;
//     public readonly PropertyValue Value;
//     public int ArgumentIndex = 0;
//     public PredicateParameter(IPredicate predicate) : this()
//     {
//         Predicate = predicate;
//         Type = PredicateParameterType.Predicate;
//     }
//     public PredicateParameter(PropertyValue value) : this()
//     {
//         Value = value;
//         Type = PredicateParameterType.Value;
//     }
//
//     private PredicateParameter(int argumentIdx) : this()
//     {
//         ArgumentIndex = argumentIdx;
//         Type = PredicateParameterType.Argument;
//     }
//
//     public static PredicateParameter Argument(int idx) => new PredicateParameter(idx);
//     public static explicit operator PredicateParameter(PropertyValue v) => new PredicateParameter(v);
//     public readonly PropertyValue GetValue(PredicateContext ctx)
//     {
//         switch (Type)
//         {
//
//             case PredicateParameterType.Value:
//                 return Value;
//             case PredicateParameterType.Predicate:
//                 return ctx.Query(Predicate, out var val) ? val : default;
//             case PredicateParameterType.Argument:
//                 return ctx.Argument(ArgumentIndex);
//             default:
//                 throw new ArgumentOutOfRangeException();
//         }
//     }
//     public bool MakeTrue(PredicateContext ctx)
//     {
//         ctx.SetArgument(ArgumentIndex, ctx.Query(Predicate, out var val) ? val : default);
//         return true;
//     }
// }