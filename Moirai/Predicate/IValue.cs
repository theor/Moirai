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
    public readonly int VarIndex;
    public readonly string Label;
    public readonly IValue Value;

    public Display(EntityType referencedType, int varIndex, string label, IValue value)
    {
        ReferencedType = referencedType;
        VarIndex = varIndex;
        Label = label;
        Value = value;
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


public interface IFunctionDescriptor
{
    string FuncName { get; }
    string Print(StoryPrinter printer, IValueCall valueCallParsed);
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


