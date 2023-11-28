using System.Text;

public struct AssignPick : IValueCall
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

    public PropertyValue Compute(PredicateContext ctx)
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

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    int? IValueCall.VariableIndex => VariableIndex;

    public string Print(StoryPrinter printer, int indent)
    {
        
        var b = new StringBuilder(FunctionDescriptor?.Print(printer, this));
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
