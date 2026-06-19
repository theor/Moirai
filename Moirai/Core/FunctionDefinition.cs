using Moirai.Core;

public record struct FunctionDefinitionId(ushort Id)
{
    public bool IsValid => Id != 0;
}
public readonly struct FunctionDefinition
{
    public readonly FunctionDefinitionId Id;
    public readonly string Name;
    public readonly EntityTypeId InstanceType;
    public readonly PropertyValue.ValueType ReturnType;
    public readonly IInstruction[] Instructions;
    public readonly Parameter[] Parameters;

    /// Lexical variable-scope tree for the debugger (null when not parsed for debugging info).
    public readonly DebugScope? DebugScopeRoot;

    public bool IsInstanceMethod => InstanceType.IsValid;

    public FunctionDefinition(FunctionDefinitionId id, string name, EntityTypeId instanceType, PropertyValue.ValueType returnType, Parameter[] parameters, IInstruction[] instructions, DebugScope? debugScopeRoot = null)
    {
        InstanceType = instanceType;
        Id = id;
        Name = name;
        ReturnType = returnType;
        Parameters = instanceType.IsValid ? Enumerable.Repeat(new Parameter("$self", Database.Instance.GetEntityType(instanceType).RefType, 0), 1).Concat(parameters).ToArray() : parameters;
        Instructions = instructions ?? new IInstruction[0];
        DebugScopeRoot = debugScopeRoot;
    }

    public readonly struct Parameter
    {
        public readonly string ParamName;
        public readonly PropertyValue.ValueType ParamType;
        public readonly int ParamIndex;

        public Parameter(string paramName, PropertyValue.ValueType paramType, int paramIndex)
        {
            ParamName = paramName;
            ParamType = paramType;
            ParamIndex = paramIndex;
        }
    }
}
