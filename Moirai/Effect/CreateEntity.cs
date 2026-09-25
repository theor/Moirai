using Moirai.Core;

public class InterpolatedStringLink : IValueCall
{
    public readonly IValue LinkValue;
    public readonly IValue LinkText;

    public InterpolatedStringLink(IValue linkValue, IValue linkText)
    {
        LinkValue = linkValue;
        LinkText = linkText;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        return $"<{LinkValue.Compute(ctx).Id}>{LinkText.Compute(ctx).Value}</>";
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return LinkValue;
        yield return LinkText;
    }
}
public class InterpolatedString : IValue
{
    public readonly string FormatString;
    public IValue[] Arguments;

    public InterpolatedString(string formatString, IValue[] arguments)
    {
        FormatString = formatString;
        Arguments = arguments;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        return ctx.Database.Printer.FormatValue(this, ctx.Database);
    }

    private string[]? _literals;
    private bool _compiled;

    /// <summary>
    /// The text around each argument: <c>Literals[i]</c> precedes argument i, the last one follows them
    /// all. Null when the format string is not the plain <c>{0}..{n-1}</c>-in-order shape the parser
    /// builds, in which case it still goes through <see cref="string.Format(string, object[])"/>.
    /// </summary>
    internal string[]? Literals
    {
        get
        {
            if (!_compiled)
            {
                _literals = Split(FormatString, Arguments.Length);
                _compiled = true;
            }

            return _literals;
        }
    }

    private static string[]? Split(string format, int count)
    {
        var literals = new string[count + 1];
        var text = new System.Text.StringBuilder();
        int next = 0;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '{' && i + 1 < format.Length && format[i + 1] == '{')
            {
                text.Append('{');
                i++;
            }
            else if (c == '}' && i + 1 < format.Length && format[i + 1] == '}')
            {
                text.Append('}');
                i++;
            }
            else if (c == '{')
            {
                var close = format.IndexOf('}', i);
                if (close < 0 || next >= count || format.Substring(i + 1, close - i - 1) != next.ToString())
                    return null;
                literals[next++] = text.ToString();
                text.Clear();
                i = close;
            }
            else if (c == '}')
                return null;
            else
                text.Append(c);
        }

        if (next != count)
            return null;
        literals[count] = text.ToString();
        return literals;
    }

    // public (string where, string? joins) ToSql(ExecuteContext ctx) => ($"'{Compute(ctx)}'", null);
}

public class MatchWeight : IValue
{
    public readonly IValue Value;
    public readonly (int,IInstruction[])[] CumulativeWeights;
    /// The story wrote no total (`random_weighted { ... }`), so <see cref="Value"/> is the sum of the
    /// weights; the printer leaves it out again.
    public readonly bool InferredTotal;

    public MatchWeight(IValue value, (int, IInstruction[])[] cumulativeWeights, bool inferredTotal = false)
    {
        Value = value;
        CumulativeWeights = cumulativeWeights;
        InferredTotal = inferredTotal;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var v = Value.Compute(ctx).IntValue;
        var r = ctx.Rnd.GenerateNext((uint)v);
        for (int i = 0; i < CumulativeWeights.Length; i++)
        {
            if(CumulativeWeights[i].Item1 == -1 || r < CumulativeWeights[i].Item1)
            {
                PropertyValue val = true;
                foreach (var instr in @CumulativeWeights[i].Item2)
                {
                    ctx.Database.DebugHook?.OnStatement(instr, ctx);
                    val = instr.Execute(ctx);
                    if (!val.BoolValue)
                        break;
                }
                return val;
            }
        }

        return true;
    }
}

public class Match : IValue
{
    public readonly IValue[] Values;
    public readonly (IValue?[], IInstruction[])[] Cases;

    public Match(IValue[] values, (IValue?[], IInstruction[])[] cases)
    {
        Values = values;
        Cases = cases;
    }

    private PropertyValue[] _values = Array.Empty<PropertyValue>();
    public PropertyValue Compute(ExecuteContext ctx)
    {
        if ( _values.Length < Values.Length)
            _values = new PropertyValue[Values.Length];
        for (var index = 0; index < Values.Length; index++)
        {
            _values[index] = Values[index].Compute(ctx);
        }
        for (var index = 0; index < Cases.Length; index++)
        {
            var @case = Cases[index];
            if (CaseMatch(_values, @case.Item1, ctx))
            {
                PropertyValue val = true;
                foreach (var instr in @case.Item2)
                {
                    ctx.Database.DebugHook?.OnStatement(instr, ctx);
                    val = instr.Execute(ctx);
                     if (!val.BoolValue)
                        break;
                }

                return val;
            }
        }

        return true;
    }

    private bool CaseMatch(PropertyValue[] actual, IValue?[] caseValues, ExecuteContext ctx)
    {
        for (int i = 0; i < caseValues.Length; i++)
        {
            var a = actual[i];
            var t = caseValues[i];

            // _ is any value
            if (t is MatchAnyValue)
                continue;
           
            if (a != t.Compute(ctx))
                return false;
        }

        return true;
    }
}

public class If : IValue
{
    public readonly IValue Condition;
    public readonly IInstruction[]? IfTrue;
    public readonly IInstruction[]? IfFalse;

    public If(IValue condition, IInstruction[]? ifTrue, IInstruction[]? ifFalse)
    {
        Condition = condition;
        IfTrue = ifTrue;
        IfFalse = ifFalse;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var scope = Condition.Compute(ctx).BoolValue ? IfTrue : IfFalse;
        PropertyValue res = true;
        foreach (var instr in scope)
        {
            ctx.Database.DebugHook?.OnStatement(instr, ctx);
            res = instr.Execute(ctx);
            if (!res.BoolValue)
               break;
        }

        return res;
    }
}

public class Mark(IValue entity, int eventIndex) : IValueCall
{
    public readonly IValue Entity = entity;
    public readonly int EventIndex = eventIndex;

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var e = Entity.Compute(ctx);
        if (e.Id.IsNull)
            return true;
        
        ctx.Mark(e.Id, EventIndex);
        return true;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return Entity;
        // yield return new Literal(EventIndex);
    }
}
public class SinceLast(IValue Entity, int EventIndex) : IValueCall, IValueSql
{
    public PropertyValue Compute(ExecuteContext ctx)
    {
        var e = Entity.Compute(ctx);
        if (e.Id.IsNull)
            return int.MaxValue;
        if(ctx.GetLastMarked(e.Id, EventIndex, out var year))
            return ctx.Year - year;
        // Never marked: treat it as "marked at year 0", i.e. `year - 0 = year`, so since_last is a large
        // positive (a long time ago) rather than a sentinel that would flip comparison signs.
        return ctx.Year;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return Entity;
        // yield return new Literal(EventIndex);
    }
}
public class Record : IValueCall
{
    public InterpolatedString String;
    // record('...', weight): any number expression, so a story can weigh a record by what happened
    // (a king's death more than a farmer's). Null means Database.Record.DefaultWeight.
    public IValue? Weight;

    public Record(InterpolatedString str, IValue? weight = null)
    {
        String = str;
        Weight = weight;
    }

    // Reused across firings, and taken while in use, so a record whose argument somehow records again
    // gets a list of its own rather than sharing this one.
    private List<EntityId>? _participants;

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var participants = _participants ?? new List<EntityId>();
        _participants = null;
        participants.Clear();
        var printer = ctx.Database.Printer;
        var sb = printer.RentBuilder();
        try
        {
            if (printer.AppendFormat(sb, String, ctx.Database, true, participants))
            {
                var weight = Weight?.Compute(ctx).IntValue ?? Database.Record.DefaultWeight;
                ctx.Database.AppendRecord(sb, participants, weight);
            }
            else
            {
                var text = printer.FormatComposite(String, ctx.Database, true, participants);
                var weight = Weight?.Compute(ctx).IntValue ?? Database.Record.DefaultWeight;
                ctx.Database.AppendRecord(text, ctx.Year, participants, weight);
            }
        }
        finally
        {
            printer.ReturnBuilder(sb);
        }

        _participants = participants;
        return true;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return String;
        if (Weight != null)
            yield return Weight;
    }
}

public class CreateEntity : IValueCall
{
    public readonly int VariableIndex;
    public readonly EntityTypeId Type;
    public readonly InterpolatedString? Name;
    // Optional object-initializer block effects, run against the new entity right after allocation.
    public readonly IInstruction[]? Init;

    public CreateEntity(int variableIndex, EntityTypeId type, InterpolatedString? name, IInstruction[]? init = null)
    {
        VariableIndex = variableIndex;
        Type = type;
        Name = name;
        Init = init;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        // if (!ctx.Database.EntityExists(ctx.EntityId))
        PropertyValue? name = null;
        if (Name != null)
        {
            name = ctx.Database.Printer.FormatValue(Name, ctx.Database);
        }

        // A singleton has at most one instance, so creating one that already exists -- because a write
        // brought it into being first -- binds the existing one instead of making a second.
        EntityId entity;
        if (ctx.Database.GetEntityType(Type).IsSingleton && ctx.Database.TryGetSingleton(Type, out var existing))
        {
            entity = existing;
            if (name is { } n)
                ctx.Database.SetProperty(entity, Database.PropName, n);
        }
        else
            entity = ctx.Database.AllocateEntity(Type, name ?? default);
        ctx.SetArgument(VariableIndex, entity);
        if (Init != null)
            foreach (var instruction in Init)
            {
                ctx.Database.DebugHook?.OnStatement(instruction, ctx);
                instruction.Execute(ctx);
            }
        return entity;
    }

    (int, PropertyValue.ValueType)? IValueCall.VariableIndex => (VariableIndex, new PropertyValue.ValueType(PropertyValue.ValueBaseType.Ref, (ushort)Type.Id));
    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        // yield return new Literal(Type);
        if(Name != null)
            yield return Name;
    }

    // Render the call plus, if present, the `{ prop := value ... }` initializer block (round-trip).
    public string Print(StoryPrinter printer, int indent)
    {
        var b = new System.Text.StringBuilder(FunctionDescriptor?.Print(printer, this));
        if (Init is { Length: > 0 })
        {
            b.AppendLine(" {");
            foreach (var effect in Init)
                printer.PrintEffect(effect, b, indent + 1);
            b.Append(StoryPrinter.IndentStr(indent) + "}");
        }

        return b.ToString();
    }
}
