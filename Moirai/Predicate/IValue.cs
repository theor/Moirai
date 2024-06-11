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
    public readonly FunctionDefinition Definition;
    public readonly IValue[] Arguments;
    public UserFunctionCall(FunctionDefinition definition, IValue[] arguments)
    {
        Definition = definition;
        Arguments = arguments;
    }

    public PropertyValue Compute(PredicateContext ctx, PropertyValue instance)
    {
        // TODO alloc
        var arguments = new PropertyValue[Definition.Parameters.Length];
        
        // f($0): need to evaluate $0 before creating a scope setting the ctx.ValueOffset
        int offset = Definition.IsInstanceMethod ? 1 : 0;
        for (int i = 0; i < Definition.Parameters.Length; i++)
        {
            var p = Definition.Parameters[i];
            arguments[i] = Definition.IsInstanceMethod && i == 0 ? instance : Arguments[i - offset]?.Compute(ctx) ?? default;
        }
        using var s = ctx.RunScope(true);
        for (int i = 0; i < Definition.Parameters.Length; i++)
        {
            var p = Definition.Parameters[i];
            ctx.SetArgument(p.ParamIndex, arguments[i]);
        }
        // TODO use default of return type ?
        PropertyValue val = default;
        foreach (var definitionInstruction in Definition.Instructions)
        {
            val = definitionInstruction.Execute(ctx);
        }

        return val;
    }
    public PropertyValue Compute(PredicateContext ctx)
    {
        return Compute(ctx, default);
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        if (Definition.Instructions.Length == 1 && Definition.Instructions[0] is CallInstruction call)
        {
            using var _ = ctx.RunScope(true);
            return call.Value.ToSql(ctx);
        }

        throw new NotImplementedException();
    }

    public IFunctionDescriptor? FunctionDescriptor
    {
        get => new UserFunctionDescriptor(Definition);
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


