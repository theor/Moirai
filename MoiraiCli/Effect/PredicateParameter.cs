using Pcg.Core;

public struct Literal : IValue
{
    public readonly PropertyValue Value;
    public Literal(PropertyValue value)
    {
        Value = value;
    }
    public PropertyValue Compute(PredicateContext ctx) => Value;
}


public struct PropertyPath : IValue
{
    public PropertyPath(int variableIndex, PropertyId? property = null)
    {
        VariableIndex = variableIndex;
        Property = property ?? PropertyId.Null;
    }

    public readonly int VariableIndex;
    public readonly PropertyId Property;
    public PropertyValue Compute(PredicateContext ctx)
    {
        var varValue = ctx.Argument(VariableIndex);
        if (!ctx.Database.TryGetEntity(varValue.IntValue, out var e))
            return default;
        if (Property == PropertyId.Null)
            return varValue;

        return e.GetProperty(Property);
    }
}
public struct YearsPassed : IValue
{
    public readonly int Years;
    public YearsPassed(int years)
    {
        Years = years;

    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }
}
public struct AssertInstr : IInstruction
{
    public readonly IValue Value;
    public readonly string Message;
    public AssertInstr(IValue value, string message)
    {
        Value = value;
        Message = message;
    }
    public bool Execute(PredicateContext ctx)
    {
        ctx.Assert(Value.Compute(ctx).BoolValue, Message);
        return true;
    }
}
public struct RandomCall : IValue
{
    public readonly ushort EnumID;
    public RandomCall(ushort enumId)
    {
        EnumID = enumId;
    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        var def = ctx.Database.Enums[EnumID];
        return def.GetRandomValue(ctx.Rnd);
    }
}


public enum CallType
{
    None,
    Pick,
    Each,
    When
}
public struct AssignPick : IInstruction
{
    public readonly int VariableIndex;
    public readonly IValue Value;
    public readonly CallType CallType;
    public readonly IInstruction[]? ScopeEffects;
    private List<EntityId>? _pool;
    public AssignPick(int variableIndex, IValue value, CallType callType, IInstruction[]? scopeEffects = null)
    {
        VariableIndex = variableIndex;
        Value = value;
        CallType = callType;
        ScopeEffects = scopeEffects;
        _pool = null;
    }
    
    public bool Execute(PredicateContext ctx)
    {
        switch (CallType)
        {

            case CallType.Pick:
            {
                bool res = ctx.PickRandom(Value, out var val);
                ctx.SetArgument(VariableIndex, val);
                return res;
            }
            case CallType.Each:
            {
                _pool ??= new();
                if (ScopeEffects != null)
                {
                    if(ctx.FindAll(Value, ref _pool))
                        foreach (var entityId in _pool)
                        {
                            ctx.SetArgument(VariableIndex, entityId);
                            if(!ScopeEffects.All(e => e.Execute(ctx))) continue;
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