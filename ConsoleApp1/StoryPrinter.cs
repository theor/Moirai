using System.Text;
using Pcg.Core;

public static class StoryPrinter
{
    public static string Print(List<Action> actions, List<string> properties)
    {
        StringBuilder sb = new();
        foreach (string property in properties.Skip(Database.DefaultProperties().Count))
        {
            // TODO types
            sb.AppendLine($"prop {property} = bool");
        }
        foreach (var action in actions)
        {
            sb.AppendLine($"{(action.IsEvent ? "event" : "rule")} {action.Name} {{");
            foreach (var when in action.Whens)
            {
                sb.AppendLine($"  when ${when.VariableIndex}: {Print(when.Predicate, properties)}");

            }
            foreach (var effect in action.Effects)
            {
                PrintEffect(properties, effect, sb, 1);
            }
            sb.AppendLine("}");

        }
        return sb.ToString();
    }
    private static void PrintEffect(List<string> properties, IEffect effect, StringBuilder sb, int indent)
    {
        string indentStr = new string(' ', indent * 2);
        switch (effect)
        {
            case CreateEntity createEntity:
                sb.AppendLine($"{indentStr}create ${createEntity.VariableIndex}: {createEntity.Type.ToString().ToLowerInvariant()}");
                break;
            // case NameEntity nameEntity:
            case AssignPick predicateParameter:
                sb.Append(
                    $"{indentStr}{predicateParameter.CallType.ToString().ToLowerInvariant()} ${predicateParameter.VariableIndex}: {Print(predicateParameter.Predicate, properties)}");
                if (predicateParameter.ScopeEffects != null)
                {
                    sb.AppendLine($"{indentStr}{{");
                    foreach (var nestedEffect in predicateParameter.ScopeEffects)
                    {
                        PrintEffect(properties, nestedEffect, sb, indent + 1);
                    }
                    sb.AppendLine($"{indentStr}}}");
                }
                else
                    sb.AppendLine();
                break;
            // case Sequence sequence:
            case SetProperty setProperty:
                sb.AppendLine(
                    $"{indentStr}set {Print(setProperty.PropertySet, properties)} = {Print(setProperty.Parameter, properties)}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect));
        }
    }
    private static string GetPropertyName(PropertyId p, List<string> properties)
    {
        if (p.IsValid && p.Id < properties.Count)
            return properties[(int)p.Id];

        return "<??>";
    }
    private static string Print(PropertyPath path, List<string> properties) =>
        path.Property != PropertyId.Null ? $"${path.VariableIndex}.{GetPropertyName(path.Property, properties)}" : $"${path.VariableIndex}";
    public static string Print(ComputedValue parameter, List<string> properties, PropertyId typeHint = default)
    {
        switch (parameter.Type)
        {

            case ComputedValue.ComputedValueType.Value:
                return Print(parameter.Value, typeHint);
            case ComputedValue.ComputedValueType.Path:
                return Print(parameter.Path, properties);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    public static string Print(PropertyValue value, PropertyId typeHint = default)
    {
        var s = value.Value;
        if (s != null)
            return s;
        if (typeHint == Database.PropType)
            return $"\"{((EntityType)value.IntValue).ToString().ToLowerInvariant()}\"";

        switch (value.Type)
        {
            case PropertyValue.ValueType.None:
                return "null";
            case PropertyValue.ValueType.String:
                return $"\"{value.Value}\"";
            case PropertyValue.ValueType.EntityId:
                if (value.IntValue == 0)
                    return "null";

                return "#" + value.IntValue;
            case PropertyValue.ValueType.Number:
                return value.IntValue.ToString();
            case PropertyValue.ValueType.Bool:
                return value.BoolValue ? "true" : "false";
            default:
                throw new ArgumentOutOfRangeException();
        }
        return value.IntValue.ToString();
    }
    public static string Print(IPredicate predicate, List<string> properties)
    {
        switch (predicate)
        {
            case And and:
                return string.Join(", ", and.Predicates.Select(predicate1 => Print(predicate1, properties)));
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
                return $"{GetPropertyName(propertyEquals.Property, properties)} {op} {Print(propertyEquals.Value, properties, propertyEquals.Property)}";
            // case True @true:
            // break;
            default:
                throw new ArgumentOutOfRangeException(nameof(predicate));

        }
    }
    public static void PrintChangeset(Changeset cs, Database db, bool oneLine = true)
    {
        void write(string s)
        {
            if (oneLine) Console.Write(s);
            else Console.WriteLine(s);
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        write(cs.ActionName);
        Console.ResetColor();
        foreach (var change in cs.Changes)
        {
            write("  " + change.ToString(db));
        }
        if (oneLine)
            Console.WriteLine();
    }
}