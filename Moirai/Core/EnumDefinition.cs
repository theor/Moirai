using Moirai;

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
}
