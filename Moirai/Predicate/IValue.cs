using Moirai.Core;

public interface IValue
{
    PropertyValue Compute(PredicateContext ctx);

    bool IsTrue(PredicateContext ctx) => Compute(ctx).BoolValue;

    (string where, string? joins) ToSql(PredicateContext ctx);
}

public class Display : IValue
{
    public readonly EntityType ReferencedType;
    public readonly int VarIndex, OtherVarIndex;
    public readonly string Label;
    public readonly IValue Value;
    public readonly InterpolatedString ItemDisplay;

    public Display(EntityType referencedType, int varIndex, int otherVarIndex, string label, IValue value, InterpolatedString itemDisplay)
    {
        ReferencedType = referencedType;
        VarIndex = varIndex;
        OtherVarIndex = otherVarIndex;
        Label = label;
        Value = value;
        ItemDisplay = itemDisplay;
    }

    public PropertyValue Compute(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }
}

public interface IValueCall : IValue
{
    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    (int,PropertyValue.ValueType)? VariableIndex => null;
    string Print(StoryPrinter printer, int indent)
    {
        return FunctionDescriptor?.Print(printer, this) ?? "";
    }
    IEnumerable<IValue> GetArgs(StoryPrinter printer);
}

public class UserFunctionCall : IValueCall
{
    private FunctionDefinition _definition;
    public readonly IValue[] Arguments;
    public UserFunctionCall(FunctionDefinition definition, IValue[] arguments)
    {
        _definition = definition;
        Arguments = arguments;
    }

    public PropertyValue Compute(PredicateContext ctx)
    {
        
        using var s = ctx.RunScope(false);
        // int valueCountIterationStart = ctx.ValueCount;
        for (int i = 0; i < _definition.Parameters.Length; i++)
        {
            var p = _definition.Parameters[i];
            ctx.SetArgument(p.ParamIndex, Arguments[i]?.Compute(ctx) ?? default);

        }
        // TODO use default of return type ?
        PropertyValue val = default;
        foreach (var definitionInstruction in _definition.Instructions)
        {
            val = definitionInstruction.Execute(ctx);
        }

        return val;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        if (_definition.Instructions.Length == 1 && _definition.Instructions[0] is CallInstruction call)
        {
            using var _ = ctx.RunScope(true);
            return call.Value.ToSql(ctx);
        }

        throw new NotImplementedException();
    }

    public IFunctionDescriptor? FunctionDescriptor
    {
        get => new UserFunctionDescriptor(_definition);
        set
        {
        }
    }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer) => Arguments;
}


public interface IFunctionDescriptor
{
    string FuncName { get; }
    string Print(StoryPrinter printer, IValueCall valueCallParsed);
}

public class UserFunctionDescriptor : IFunctionDescriptor
{
    public FunctionDefinition Definition { get; }

    public UserFunctionDescriptor(FunctionDefinition definition)
    {
        Definition = definition;
    }

    public string FuncName => Definition.Name;
    public string Print(StoryPrinter printer, IValueCall valueCallParsed)
    {
        return $"{FuncName}({(string.Join(", ",valueCallParsed.GetArgs(printer).Select(printer.Print)))})";
    }
}
public class MatchAnyValue : IValue
{
    private MatchAnyValue(){}
    public static MatchAnyValue Instance = new();
    public PropertyValue Compute(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }
}
public class IsOfType : IValue
{
    public readonly IValue Entity;
    public readonly EntityTypeId ValueTypeId;
    public IsOfType(IValue entity, EntityTypeId valueTypeId)
    {
        Entity = entity;
        ValueTypeId = valueTypeId;
    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        var typeId = Entity.Compute(ctx).TypeId;
        var result = typeId == ValueTypeId;
        Profiler.HitOfType(typeId, result);
        return result;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx) => ($"default__type = " + ValueTypeId.Id, null);
}


