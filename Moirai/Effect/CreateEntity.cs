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

    public PropertyValue Compute(PredicateContext ctx)
    {
        return $"<{LinkValue.Compute(ctx).Id}>{LinkText.Compute(ctx).Value}</>";
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
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

    public PropertyValue Compute(PredicateContext ctx)
    {
        return ctx.Database.Printer.Format(this, ctx.Database) ?? "";
    }

    public (string where, string? joins) ToSql(PredicateContext ctx) => ($"'{Compute(ctx)}'", null);
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

    public PropertyValue Compute(PredicateContext ctx)
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
                    if (instr is CallInstruction call)
                        val = call.Value.Compute(ctx);
                    else if (!instr.Execute(ctx))
                        break;
                }
                return val;
            }
        }

        return true;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
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
    public PropertyValue Compute(PredicateContext ctx)
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
                    if (instr is CallInstruction call)
                        val = call.Value.Compute(ctx);
                    else if (!instr.Execute(ctx))
                        break;
                }

                return val;
            }
        }

        return true;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }

    private bool CaseMatch(PropertyValue[] actual, IValue?[] caseValues, PredicateContext ctx)
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

    public PropertyValue Compute(PredicateContext ctx)
    {
        var scope = Condition.Compute(ctx).BoolValue ? IfTrue : IfFalse;
        PropertyValue res = true;
        foreach (var instr in scope)
        {
            if (instr is CallInstruction call)
                res = call.Value.Compute(ctx);
            else if (!instr.Execute(ctx))
                break;
        }

        return true;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }
}

public class Mark(IValue entity, int eventIndex) : IValueCall
{
    public readonly IValue Entity = entity;
    public readonly int EventIndex = eventIndex;

    public PropertyValue Compute(PredicateContext ctx)
    {
        var e = Entity.Compute(ctx);
        if (e.Id.IsNull)
            return true;
        
        ctx.Mark(e.Id, EventIndex);
        return true;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return Entity;
        // yield return new Literal(EventIndex);
    }
}
public class SinceLast(IValue Entity, int EventIndex) : IValueCall
{
    public PropertyValue Compute(PredicateContext ctx)
    {
        var e = Entity.Compute(ctx);
        if (e.Id.IsNull)
            return int.MaxValue;
        if(ctx.GetLastMarked(e.Id, EventIndex, out var year))
            return ctx.Year - year;
        return int.MinValue;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
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

    public PropertyValue Compute(PredicateContext ctx)
    {
        ctx.Database.AppendRecord(ctx.Database.Printer.Format(String, ctx.Database, true), ctx.Year,
            ctx.Database.CurrentChangeset.Categories);
        return true;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
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

    public CreateEntity(int variableIndex, EntityTypeId type, InterpolatedString? name)
    {
        VariableIndex = variableIndex;
        Type = type;
        Name = name;
    }

    public PropertyValue Compute(PredicateContext ctx)
    {
        // if (!ctx.Database.EntityExists(ctx.EntityId))
        string? name = null;
        if (Name != null)
        {
            name = ctx.Database.Printer.Format(Name, ctx.Database);
        }

        var entity = ctx.Database.AllocateEntity(Type, name);
        ctx.SetArgument(VariableIndex, entity);
        return entity;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }

    (int, PropertyValue.ValueType)? IValueCall.VariableIndex => (VariableIndex, new PropertyValue.ValueType(PropertyValue.ValueBaseType.Ref, (ushort)Type.Id));
    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        // yield return new Literal(Type);
        if(Name != null)
            yield return Name;
    }
}
