using System.Collections.Generic;
using System.Linq;
using Moirai.Core;

public interface IValue
{
    PropertyValue Compute(ExecuteContext ctx);

    bool IsTrue(ExecuteContext ctx) => Compute(ctx).BoolValue;
}

// Marker for an IValue usable as a pick/each query predicate. (Formerly also declared a ToSql member
// for the SQLite backend; that backend is gone and predicates are now evaluated in-memory via IsTrue.)
public interface IValueSql : IValue
{
}

public class Display : IValue
{
    public readonly EntityType ReferencedType;
    public readonly int VarIndex, OtherVarIndex;
    public readonly string Label;
    public readonly IValueSql Value;
    public readonly InterpolatedString ItemDisplay;

    public Display(EntityType referencedType, int varIndex, int otherVarIndex, string label, IValueSql value, InterpolatedString itemDisplay)
    {
        ReferencedType = referencedType;
        VarIndex = varIndex;
        OtherVarIndex = otherVarIndex;
        Label = label;
        Value = value;
        ItemDisplay = itemDisplay;
    }

    public PropertyValue Compute(ExecuteContext ctx)
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

public class UserFunctionCall : IValueCall, IValueSql
{
    public readonly FunctionDefinition Definition;
    public readonly IValue[] Arguments;
    public UserFunctionCall(FunctionDefinition definition, IValue[] arguments)
    {
        Definition = definition;
        Arguments = arguments;
    }

    public PropertyValue Compute(ExecuteContext ctx, PropertyValue instance)
    {
        int offset = /*Definition.IsInstanceMethod ? 1 :*/ 0;

        // f($0): need to evaluate $0 before creating a scope setting the ctx.ValueOffset
        var valueCount = ctx.ValueCount + ctx.ValueOffset;
        // don't set the offset - wait for the Start() call below
        using var s = ctx.RunScope(false);
        for (int i = 0; i < Definition.Parameters.Length; i++)
        {
            var p = Definition.Parameters[i];
            var argValue = /*Definition.IsInstanceMethod && i == 0 ? instance : */Arguments[i - offset]?.Compute(ctx) ?? default;
            // compute the current arg index by adding the valueCount, which will be the param index after the scope starts
            ctx.SetArgument(p.ParamIndex + valueCount, argValue);
        }
        s.Start();
        var hook = ctx.Database.DebugHook;
        hook?.OnEnterFrame(DebugFrameKind.Function, Definition.Name, Definition.DebugScopeRoot, ctx.ValueOffset);
        PropertyValue val = default;
        foreach (var definitionInstruction in Definition.Instructions)
        {
            hook?.OnStatement(definitionInstruction, ctx);
            val = definitionInstruction.Execute(ctx);
        }

        hook?.OnExitFrame();
        return val;
    }
    public PropertyValue Compute(ExecuteContext ctx)
    {
        return Compute(ctx, default); 
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
    bool ExpectVariable { get; }
    string Documentation { get; }
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
    public bool ExpectVariable => false;
    public string Documentation => "";

    public string Print(StoryPrinter printer, IValueCall valueCallParsed)
    {
        return $"{FuncName}({(string.Join(", ",valueCallParsed.GetArgs(printer).Select(printer.Print)))})";
    }
}
public class MatchAnyValue : IValue
{
    private MatchAnyValue(){}
    public static MatchAnyValue Instance = new();
    public PropertyValue Compute(ExecuteContext ctx)
    {
        throw new NotImplementedException();
    }
}
public class IsOfType : IValueSql
{
    public readonly IValue Entity;
    public readonly EntityTypeId ValueTypeId;
    public IsOfType(IValue entity, EntityTypeId valueTypeId)
    {
        Entity = entity;
        ValueTypeId = valueTypeId;
    }
    public PropertyValue Compute(ExecuteContext ctx)
    {
        var typeId = Entity.Compute(ctx).TypeId;
        var result = typeId == ValueTypeId;
        Profiler.HitOfType(typeId, result);
        return result;
    }
}


