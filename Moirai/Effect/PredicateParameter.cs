using Moirai.Core;

public struct Literal : IValue
{
    public readonly PropertyValue Value;
    public Literal(PropertyValue value)
    {
        Value = value;
    }
    public PropertyValue Compute(PredicateContext ctx) => Value;
    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = default;
        return false;
    }
    public string ToSql(PredicateContext ctx)
    {
        return Value.ToSql();
    }
}


public struct PropertyPath : IValue
{
    public readonly int VariableIndex;
    public readonly PropertyId Property;
    public readonly EntityTypeId SingletonType;

    public enum PropertyPathMode
    {
        Variable,
        Singleton
    }

    public readonly PropertyPathMode Mode;
    public PropertyPath(int variableIndex, PropertyId? property = null)
    {
        VariableIndex = variableIndex;
        Property = property ?? PropertyId.Null;
        Mode = PropertyPathMode.Variable;
        SingletonType = default;
    }
    public PropertyPath(EntityTypeId singletonTypeId, PropertyId propertyId)
    {
        SingletonType = singletonTypeId;
        Property = propertyId;
        Mode = PropertyPathMode.Singleton;
        VariableIndex = -1;
    }
    public bool Nested => false;

    public PropertyValue Compute(PredicateContext ctx)
    {
        if (Mode == PropertyPathMode.Singleton)
        {
            if (!ctx.GetSingleton(SingletonType, out var entity))
                return default;
            if (Property == PropertyId.Null)
                return entity.Id;

            return entity.GetProperty(Property);
        }

        PropertyValue varValue = ctx.Argument(VariableIndex);
        if (varValue.Type != PropertyValue.TypeRef)
            return varValue;
        if (!ctx.Database.TryGetEntity(varValue.Id, out var e))
            return default;
        if (Property == PropertyId.Null)
            return varValue;

        return e.GetProperty(Property);
    }
    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = default;
        return false;
    }
    public string ToSql(PredicateContext ctx)
    {
        // TODO must be contextual - if var is the one assigned, should be prop name, otherwise computed
        if (Mode == PropertyPathMode.Variable)
            return /*Property.IsValid ?*/ ctx.Database.GetPropertyName(Property);// : Compute(ctx).ToSql();
        return Compute(ctx).ToSql();
    }
}


public struct AssertInstr : IInstruction
{
    public enum AssertMode
    {
        True,
        Eq,
    }

    public readonly AssertMode Mode;
    public readonly IValue Value;
    public readonly IValue? Right;
    public readonly string Message;
    public AssertInstr(IValue value, string message) : this()
    {
        Mode = AssertMode.True;
        Value = value;
        Message = message;
    }
    public AssertInstr(IValue left, IValue? right, string message) : this(left, message)
    {
        Mode = AssertMode.Eq;
        Right = right;
    }
    public bool Execute(PredicateContext ctx)
    {
        switch (Mode)
        {

            case AssertMode.True:
                ctx.Assert(Value.Compute(ctx).BoolValue, Message);
                break;
            case AssertMode.Eq:
                PropertyValue left = Value.Compute(ctx);
                PropertyValue right = Right.Compute(ctx);
                ctx.Assert(left == right,
                    $"{Message}, actual values:\n     left: {ctx.Database.Printer.Print(left)}\n    right: {ctx.Database.Printer.Print(right)}");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
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
    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = default;
        return false;
    }
    public string ToSql(PredicateContext ctx)
    {
        return Compute(ctx).ToSql();
    }
}

public struct RandomName : IValue
{
    public enum NameType
    {
        Name,
        Item
    }

    public readonly NameType Type;
    public RandomName(NameType type)
    {
        Type = type;
    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        return Type == NameType.Name ? NameEntity.Names.RandomIn(ctx.Rnd) : NameEntity.Items.RandomIn(ctx.Rnd);
    }
    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = default;
        return false;
    }
    public string ToSql(PredicateContext ctx)
    {
        return Compute(ctx).ToSql();
    }
}


public enum CallType
{
    None,
    Pick,
    Each,
    When
}

public struct CallRule : IInstruction
{
    public readonly int VariableIndex;
    public readonly int RuleIndex;
    public readonly int Count;
    public CallRule(int variableIndex, int ruleIndex, int count)
    {
        RuleIndex = ruleIndex;
        Count = count;
        VariableIndex = variableIndex;

    }
    public bool Execute(PredicateContext ctx)
    {
        // DONE offset value stack
        // eg. if $0 $1 are used now, have called.$0 become $2
        // copy result in VariableIndex then pop extra values
        bool res = false;
        PropertyValue ctxLastValue = default;
        for (int i = 0; i < Count; i++)
            using (ctx.RunScope())
            {
                res = ctx.Database.RunAction(ctx.Database.Actions[RuleIndex]);
                ctxLastValue = ctx.LastValue;
            }
        ctx.SetArgument(VariableIndex, ctxLastValue);
        return res;
    }
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
                // Console.WriteLine($"PICK {ctx.Database.Printer.Print(Value)}");
                bool res = ctx.PickRandom(Value, out var val);
                ctx.SetArgument(VariableIndex, val);
                // Console.WriteLine($"ENDPICK {ctx.Database.Printer.Print(Value)} VAL COUNT {ctx.ValueCount} OFFSET {ctx.ValueOffset}");
                return res;
            }
            case CallType.Each:
            {
                _pool ??= new();
                if (ScopeEffects != null)
                {
                    // Console.ForegroundColor = ConsoleColor.Blue;
                    // Console.WriteLine($"FIND ALL {ctx.Database.Printer.Print(Value)} VAL COUNT {ctx.ValueCount} OFFSET {ctx.ValueOffset}");
                    // Console.ResetColor(); 
                    if (ctx.Database.FindAll(Value, ref _pool))
                    {
                        for (var index = 0; index < _pool.Count; index++)
                        {
                            int valueCountIterationStart = ctx.ValueCount;
                            var entityId = _pool[index];
                            ctx.SetArgument(VariableIndex, entityId);
                            // Console.WriteLine($"{index + 1} / {_pool.Count} VAL COUNT {ctx.ValueCount} OFFSET {ctx.ValueOffset}");
                            foreach (var e in ScopeEffects)
                            {
                                // Console.WriteLine("  Exec " + ctx.Database.Printer.PrintEffect(e));
                                if (!e.Execute(ctx))
                                {
                                    break;
                                }
                            }
                            while (ctx.ValueCount > valueCountIterationStart)
                                ctx.PopArgument();
                        }
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