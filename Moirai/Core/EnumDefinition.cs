using Moirai;

public readonly struct EnumDefinition
{
    public readonly string Name;
    public readonly List<string> Values;
    public readonly List<string> FormattedValues;
    public readonly ushort Index;
    public PropertyValue.ValueType ValueType => Index != 0 ? PropertyValue.TypeEnum(Index) : default;
    public EnumDefinition(ushort index, string name, List<string> values)
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
                propertyValue = new PropertyValue { IntValue = i+1, Type = ValueType };
                return true;
            }
        }
        propertyValue = default;
        return false;
    }
    public PropertyValue GetRandomValue(Pcg32 rnd)
    {
        var i = rnd.GenerateNext((uint)Values.Count);
        return new PropertyValue { IntValue = i+1, Type = ValueType };
    }
}