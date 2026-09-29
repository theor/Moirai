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
    public readonly Parameter[] Parameters;

    /// The body, shared by every copy of this struct. Every function is declared before any body is parsed,
    /// so a call can be parsed before the body it runs, and it reads the body through this.
    private readonly Body _body;
    public IInstruction[] Instructions => _body?.Instructions ?? [];

    /// Lexical variable-scope tree for the debugger (null when not parsed for debugging info).
    public DebugScope? DebugScopeRoot => _body?.DebugScopeRoot;

    /// Fills in the body of a function declared without one.
    public void SetBody(IInstruction[] instructions, DebugScope? debugScopeRoot)
    {
        _body.Instructions = instructions;
        _body.DebugScopeRoot = debugScopeRoot;
    }

    private sealed class Body
    {
        public IInstruction[] Instructions = [];
        public DebugScope? DebugScopeRoot;
    }

    public bool IsInstanceMethod => InstanceType.IsValid;

    public FunctionDefinition(FunctionDefinitionId id, string name, EntityTypeId instanceType, PropertyValue.ValueType returnType, Parameter[] parameters, IInstruction[]? instructions = null, DebugScope? debugScopeRoot = null)
    {
        InstanceType = instanceType;
        Id = id;
        Name = name;
        ReturnType = returnType;
        Parameters = instanceType.IsValid ? Enumerable.Repeat(new Parameter("$self", Database.Instance.GetEntityType(instanceType).RefType, 0), 1).Concat(parameters).ToArray() : parameters;
        _body = new Body { Instructions = instructions ?? [], DebugScopeRoot = debugScopeRoot };
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
