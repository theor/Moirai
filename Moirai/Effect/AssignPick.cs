using System.Text;
using Moirai.Core;

public struct AssignPick : IValueCall
{
    public readonly EntityTypeId EntityType;
    public readonly int VariableIndex;
    public readonly IValueSql Value;
    public readonly CallType CallType;
    public readonly IInstruction[]? ScopeEffects;
    /// A pick's `else { ... }`: what runs when nothing matches, before the rule stops as a failed pick
    /// always does -- so the rule still counts as not completed on the Rules page.
    public readonly IInstruction[]? ElseEffects;
    private List<EntityId>? _pool;
    public AssignPick(EntityTypeId entityType, int variableIndex, IValueSql value, CallType callType,
        IInstruction[]? scopeEffects = null, IInstruction[]? elseEffects = null)
    {
        EntityType = entityType;
        VariableIndex = variableIndex;
        Value = value;
        CallType = callType;
        ScopeEffects = scopeEffects;
        ElseEffects = elseEffects;
        _pool = null;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        switch (CallType)
        {

            case CallType.Pick:
            {
                // Console.WriteLine($"PICK {ctx.Database.Printer.Print(Value)}");
                bool res = ctx.PickRandom(EntityType, Value, VariableIndex, out var val);
                ctx.SetArgument(VariableIndex, val);
                if (!res && ElseEffects != null)
                {
                    foreach (var e in ElseEffects)
                    {
                        ctx.Database.DebugHook?.OnStatement(e, ctx);
                        if (!e.Execute(ctx).BoolValue)
                            break;
                    }
                }
                // Console.WriteLine($"ENDPICK {ctx.Database.Printer.Print(Value)} VAL COUNT {ctx.ValueCount} OFFSET {ctx.ValueOffset}");
                return res;
            }
            case CallType.Each:
            {
                // The site's list is taken while its bodies run and handed back after, so an each whose body
                // reaches this same site again (a call() back into its own event) gets a list of its own
                // instead of FindAll clearing the one being walked.
                var pool = _pool ?? new List<EntityId>();
                _pool = null;
                if (ScopeEffects != null)
                {
                    if (ctx.Database.FindAll(EntityType, Value, VariableIndex, ref pool))
                    {
                        for (var index = 0; index < pool.Count; index++)
                        {
                            using var s = ctx.RunScope(false);
                            // int valueCountIterationStart = ctx.ValueCount;
                            var entityId = pool[index];
                            ctx.SetArgument(VariableIndex, entityId);
                            // Console.WriteLine($"{index + 1} / {_pool.Count} VAL COUNT {ctx.ValueCount} OFFSET {ctx.ValueOffset}");
                            foreach (var e in ScopeEffects)
                            {
                                // Console.WriteLine("  Exec " + ctx.Database.Printer.PrintEffect(e));
                                ctx.Database.DebugHook?.OnStatement(e, ctx);
                                if (!e.Execute(ctx).BoolValue)
                                {
                                    break;
                                }
                            }
                            // ctx.ClearValueStack();
                            // while (ctx.ValueCount > valueCountIterationStart)
                            //     ctx.PopArgument();
                        }
                    }
                }

                pool.Clear();
                _pool = pool;
                return true;
            }
            default:
                throw new ArgumentOutOfRangeException(CallType.ToString());
        }
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    (int, PropertyValue.ValueType)? IValueCall.VariableIndex => (VariableIndex,  PropertyValue.TypeTypedRef(EntityType));

    public string Print(StoryPrinter printer, int indent)
    {
        
        var b = new StringBuilder(FunctionDescriptor?.Print(printer, this));
        if (ElseEffects != null)
        {
            b.AppendLine(" else {");
            foreach (var effect in ElseEffects)
                printer.PrintEffect(effect, b, indent + 1);
            b.Append(StoryPrinter.IndentStr(indent) + "}");
        }
        if (ScopeEffects != null)
        {
            b.AppendLine(" {");
            foreach (var effect in ScopeEffects)
            {
                printer.PrintEffect(effect, b, indent + 1);
            }
            b.AppendLine(StoryPrinter.IndentStr(indent) + " }") ;
        }

        return b.ToString();
    }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        switch (CallType)
        {
            case CallType.Pick:
            case CallType.Each:
                yield return Value;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
