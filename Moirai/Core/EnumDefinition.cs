using Moirai;

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

    public FunctionDefinition(FunctionDefinitionId id, string name, EntityTypeId instanceType, PropertyValue.ValueType returnType, Parameter[] parameters, IInstruction[] instructions)
    {
        InstanceType = instanceType;
        Id = id;
        Name = name;
        ReturnType = returnType;
        Parameters = parameters;
        Instructions = instructions ?? new IInstruction[0];
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
public record struct EnumDefinitionId(ushort Id);
public readonly struct EnumDefinition
{
    public readonly string Name;
    public readonly List<string> Values;
    public readonly List<string> FormattedValues;
    public readonly EnumDefinitionId Index;
    public PropertyValue.ValueType ValueType => Index.Id != 0 ? PropertyValue.TypeEnum(Index) : default;
    public PropertyValue EnumType => Index;

    public EnumDefinition(EnumDefinitionId index, string name, List<string> values)
    {
        Name = name;
        Values = values;
        FormattedValues = values.Select(Format).ToList();
        Index = index;
    }
    private string Format(string arg)
    {
        return arg.Replace('_', ' ');
    }
    public bool GetValueFromName(string valueName, out PropertyValue propertyValue)
    {
        for (int i = 0; i < Values.Count; i++)
        {
            if (Values[i] == valueName)
            {
                propertyValue = new PropertyValue(ValueType, i+1 );
                return true;
            }
        }
        propertyValue = default;
        return false;
    }
    public PropertyValue GetRandomValue(Pcg32 rnd)
    {
        var i = rnd.GenerateNext((uint)Values.Count);
        return new PropertyValue(ValueType, i+1 );
    }

    public static EnumDefinition FromEnum<T>(EnumDefinitionId enumDefinitionId) where T: struct, Enum
    {
        return new EnumDefinition(enumDefinitionId, typeof(T).Name, Enum.GetNames<T>().ToList());
    }
}
