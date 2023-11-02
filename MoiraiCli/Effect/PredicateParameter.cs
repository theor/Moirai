using Pcg.Core;

public struct ComputedValue
{
    public struct RandomCall
    {
        public readonly ushort EnumID;
        public RandomCall(ushort enumId)
        {
            EnumID = enumId;
        }
    }
    public enum ComputedValueType
    {
        Value,
        Path,
        Random,
    }

    public readonly ComputedValueType Type;
    public readonly PropertyValue Value;
    public readonly PropertyPath Path;
    public readonly RandomCall Random;
    public ComputedValue(EnumDefinition enumDefinition) : this()
    {
        Type = ComputedValueType.Random;
        Random = new RandomCall(enumDefinition.Index);
    }
    public ComputedValue(PropertyValue value) : this()
    {
        Type = ComputedValueType.Value;
        Value = value;
    }
    public ComputedValue(PropertyPath path) : this()
    {
        Type = ComputedValueType.Path;
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
    public readonly IEffect[]? ScopeEffects;
    private List<EntityId>? _pool;
    public AssignPick(int variableIndex, IPredicate value, CallType callType, IEffect[]? scopeEffects = null)
    {
        VariableIndex = variableIndex;
        Predicate = value;
        CallType = callType;
        ScopeEffects = scopeEffects;
        _pool = null;
    }
    
    public bool MakeTrue(PredicateContext ctx)
    {
        switch (CallType)
        {

            case CallType.Pick:
            {
                bool res = ctx.PickRandom(Predicate, out var val);
                ctx.SetArgument(VariableIndex, val);
                return res;
            }
            case CallType.Each:
            {
                _pool ??= new();
                if (ScopeEffects != null)
                {
                    if(ctx.FindAll(Predicate, ref _pool))
                        foreach (var entityId in _pool)
                        {
                            ctx.SetArgument(VariableIndex, entityId);
                            if(!ScopeEffects.All(e => e.MakeTrue(ctx))) continue;
                        }
                }
                return true;
            }
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