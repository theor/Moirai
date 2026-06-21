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
        return ctx.Database.Printer.Format(this, ctx.Database) ?? "";
    }

    // public (string where, string? joins) ToSql(ExecuteContext ctx) => ($"'{Compute(ctx)}'", null);
}

public class MatchWeight : IValue
{
    public readonly IValue Value;
    public readonly (int,IInstruction[])[] CumulativeWeights;

    public MatchWeight(IValue value, (int, IInstruction[])[] cumulativeWeights)
    {
        Value = value;
        CumulativeWeights = cumulativeWeights;
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
        return int.MinValue;
    }

    public (string where, string? joins) ToSql(ExecuteContext ctx)
    {
        return ($"({ctx.Year} - COALESCE(marked.last_year, 0))",
        $"LEFT JOIN marked ON marked.eid = entity.default__id AND marked.marker = {EventIndex}");
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

    public Record(InterpolatedString str)
    {
        String = str;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var participants = new List<EntityId>();
        var text = ctx.Database.Printer.Format(String, ctx.Database, true, participants);
        ctx.Database.AppendRecord(text, ctx.Year, participants);
        return true;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return String;
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
        string? name = null;
        if (Name != null)
        {
            name = ctx.Database.Printer.Format(Name, ctx.Database);
        }

        var entity = ctx.Database.AllocateEntity(Type, name);
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
