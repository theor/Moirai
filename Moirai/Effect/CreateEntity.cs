using Moirai.Core;

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

    public bool HasTypeFilter(out EntityTypeId type)
    {
        type = default;
        return false;
    }

    public string ToSql(PredicateContext ctx) => $"'{Compute(ctx)}'";
}

public class MatchWeight : IInstruction
{
    public readonly IValue Value;
    public readonly (int,IInstruction[])[] CumulativeWeights;

    public MatchWeight(IValue value, (int, IInstruction[])[] cumulativeWeights)
    {
        Value = value;
        CumulativeWeights = cumulativeWeights;
    }

    public bool Execute(PredicateContext ctx)
    {
        var v = Value.Compute(ctx).IntValue;
        var r = ctx.Rnd.GenerateNext((uint)v);
        for (int i = 0; i < CumulativeWeights.Length; i++)
        {
            if(CumulativeWeights[i].Item1 == -1 || r < CumulativeWeights[i].Item1)
            {
                foreach (var instr in @CumulativeWeights[i].Item2)
                {
                    if (!instr.Execute(ctx))
                        break;
                }
                break;
            }
        }

        return true;
    }
}

public class Match : IInstruction
{
    public readonly IValue[] Values;
    public readonly (IValue?[], IInstruction[])[] Cases;

    public Match(IValue[] values, (IValue?[], IInstruction[])[] cases)
    {
        Values = values;
        Cases = cases;
    }

    private PropertyValue[] _values = Array.Empty<PropertyValue>();
    public bool Execute(PredicateContext ctx)
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
                foreach (var instr in @case.Item2)
                {
                    if (!instr.Execute(ctx))
                        break;
                }

                break;
            }
        }

        return true;
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

public class If : IInstruction
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

    public bool Execute(PredicateContext ctx)
    {
        var scope = Condition.Compute(ctx).BoolValue ? IfTrue : IfFalse;
        foreach (var instr in scope)
        {
            if (!instr.Execute(ctx))
                break;
        }

        return true;
    }
}

public class FormatAction : IInstruction
{
    public InterpolatedString String;

    public FormatAction(InterpolatedString str)
    {
        String = str;
    }

    public bool Execute(PredicateContext ctx)
    {
        ctx.Database.AppendRecord(ctx.Database.Printer.Format(String, ctx.Database, true), ctx.Year,
            ctx.Database.CurrentChangeset.Categories);
        return true;
    }
}

public class CreateEntity : IInstruction
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

    public bool Execute(PredicateContext ctx)
    {
        // if (!ctx.Database.EntityExists(ctx.EntityId))
        string? name = null;
        if (Name != null)
        {
            name = ctx.Database.Printer.Format(Name, ctx.Database);
        }

        var entity = ctx.Database.AllocateEntity(Type, name);
        ctx.SetArgument(VariableIndex, entity);
        return true;
    }
}
