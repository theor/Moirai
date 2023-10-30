using System.Text;

public static class StoryPrinter
{
    public static string Print(List<Action> actions)
    {
        StringBuilder sb = new();
        foreach (var action in actions)
        {
            sb.AppendLine("@" + action.Name);
            foreach (var effect in action.Effects)
            {
                switch (effect)
                {
                    case CreateEntity createEntity:
                        sb.AppendLine($"  ${createEntity.VariableIndex} = create {createEntity.Type.ToString().ToLowerInvariant()}");
                        break;
                    // case NameEntity nameEntity:
                    case AssignPick predicateParameter:
                        sb.AppendLine($"  ${predicateParameter.VariableIndex} = pick {Print(predicateParameter.Predicate)}");
                        break;
                    // case Sequence sequence:
                    case SetProperty setProperty:
                        sb.AppendLine(
                            $"  set {Print(setProperty.PropertySet)} = {Print(setProperty.Parameter)}");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(effect));
                }
            }
            sb.AppendLine();

        }
        return sb.ToString();
    }
    private static string Print(PropertyPath path) => path.Property.HasValue ? $"${path.VariableIndex}.{path.Property}" : $"${path.VariableIndex}";
    public static string Print(ComputedValue parameter, PropertyType? typeHint = null)
    {
        switch (parameter.Type)
        {

            case ComputedValue.ComputedValueType.Value:
                return Print(parameter.Value, typeHint);
            case ComputedValue.ComputedValueType.Path:
                return Print(parameter.Path);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    public static string Print(PropertyValue value, PropertyType? typeHint = null)
    {
        var s = value.Value;
        if (s != null)
            return s;
        if (typeHint == PropertyType.Type)
            return $"\"{((EntityType)value.IntValue).ToString().ToLowerInvariant()}\"";

        switch (value.Type)
        {

            case PropertyValue.ValueType.String:
                return $"\"{value.Value}\"";
            case PropertyValue.ValueType.EntityId:
                if (value.IntValue == 0)
                    return "null";
                return "#"+value.IntValue;
            case PropertyValue.ValueType.Number:
                return value.IntValue.ToString();
            case PropertyValue.ValueType.Bool:
                return value.BoolValue ? "true" : "false";
            default:
                throw new ArgumentOutOfRangeException();
        }
        return value.IntValue.ToString();
    }
    public static string Print(IPredicate predicate)
    {
        switch (predicate)
        {
            case And and:
                return string.Join(", ", and.Predicates.Select(Print));
            // case EntityExists entityExists:
            // break;
            // case HasProperty hasProperty:
            // break;
            case PropertyOperator propertyEquals:
                string op = propertyEquals.Op switch
                {

                    PropertyOperator.Operator.Equals => "=",
                    PropertyOperator.Operator.NotEquals => "!=",
                    _ => throw new ArgumentOutOfRangeException()
                };
                return $"{propertyEquals.Property} {op} {Print(propertyEquals.Value, propertyEquals.Property)}";
            // case True @true:
            // break;
            default:
                throw new ArgumentOutOfRangeException(nameof(predicate));

        }
    }
}