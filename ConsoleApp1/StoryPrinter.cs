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
                        sb.AppendLine("  create " + createEntity.Type);
                        break;
                    // case NameEntity nameEntity:
                    case PredicateParameter predicateParameter:
                        sb.AppendLine($"  ${predicateParameter.ArgumentIndex} = pick({Print(predicateParameter)})");
                        break;
                    // case Sequence sequence:
                    case SetProperty setProperty:
                        sb.AppendLine(
                            $"  set ${setProperty.Target}.{setProperty.Property} = {Print(setProperty.Parameter)}");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(effect));
                }
            }
            sb.AppendLine();

        }
        return sb.ToString();
    }
    public static string Print(PredicateParameter parameter, PropertyType? typeHint = null)
    {
        switch (parameter.Type)
        {

            case PredicateParameter.PredicateParameterType.Value:
                return Print(parameter.Value, typeHint);
            case PredicateParameter.PredicateParameterType.Predicate:
                return Print(parameter.Predicate!);
            case PredicateParameter.PredicateParameterType.Argument:
                return $"${parameter.ArgumentIndex}";
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
            return ((EntityType)value.IntValue).ToString();

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
            case PropertyEquals propertyEquals:
                return $"{propertyEquals.Property} = {Print(propertyEquals.Value, propertyEquals.Property)}";
            case PropertyNotEquals propertyNotEquals:
                return $"{propertyNotEquals.Property} != {Print(propertyNotEquals.Value, propertyNotEquals.Property)}";
            // case True @true:
            // break;
            default:
                throw new ArgumentOutOfRangeException(nameof(predicate));

        }
    }
}