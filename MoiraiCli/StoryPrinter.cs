using System.Text;
using Pcg.Core;

public class StoryPrinter
{
    private readonly Database _database;
    public StoryPrinter(Database database)
    {
        _database = database;
    }
    public string Print()
    {
        StringBuilder sb = new();
        foreach (EntityType type in _database.Types.Skip(1))
        {
            sb.AppendLine($"entity {type.Name} {{ }}");

        }
        foreach (string property in _database.Properties.Skip(Database.DefaultProperties().Count))
        {
            // TODO types
            sb.AppendLine($"prop {property} = bool");
        }
        foreach (var action in _database.Actions)
        {
            sb.AppendLine($"{(action.IsEvent ? "event" : "rule")} {action.Name} {{");
            foreach (var when in action.Whens)
            {
                sb.AppendLine($"  when ${when.VariableIndex}: {Print(when.Predicate)}");

            }
            foreach (var effect in action.Effects)
            {
                PrintEffect(effect, sb, 1);
            }
            sb.AppendLine("}");

        }
        return sb.ToString();
    }
    private void PrintEffect(IEffect effect, StringBuilder sb, int indent)
    {
        string indentStr = new string(' ', indent * 2);
        switch (effect)
        {
            case CreateEntity createEntity:
                sb.AppendLine($"{indentStr}create ${createEntity.VariableIndex}: {_database.GetEntityTypeName(createEntity.Type)}");
                break;
            // case NameEntity nameEntity:
            case AssignPick predicateParameter:
                sb.Append(
                    $"{indentStr}{predicateParameter.CallType.ToString().ToLowerInvariant()} ${predicateParameter.VariableIndex}: {Print(predicateParameter.Predicate)}");
                if (predicateParameter.ScopeEffects != null)
                {
                    sb.AppendLine($"{indentStr}{{");
                    foreach (var nestedEffect in predicateParameter.ScopeEffects)
                    {
                        PrintEffect(nestedEffect, sb, indent + 1);
                    }
                    sb.AppendLine($"{indentStr}}}");
                }
                else
                    sb.AppendLine();
                break;
            // case Sequence sequence:
            case SetProperty setProperty:
                sb.AppendLine(
                    $"{indentStr}set {Print(setProperty.PropertySet)} = {Print(setProperty.Parameter)}");
                break;
            case FormatAction formatAction:
                sb.AppendLine($"{indentStr}format \"{string.Format(formatAction.FormatString, formatAction.Arguments.Select(a => (object)($"{{{Print(a)}}}")).ToArray())}\"");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect));
        }
    }
    private string GetPropertyName(PropertyId p)
    {
        if (p.IsValid && p.Id < _database.Properties.Count)
            return _database.Properties[(int)p.Id];

        return "<??>";
    }
    private string Print(PropertyPath path) =>
        path.Property != PropertyId.Null ? $"${path.VariableIndex}.{GetPropertyName(path.Property)}" : $"${path.VariableIndex}";
    public string Print(ComputedValue parameter, PropertyId typeHint = default)
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
    public string Print(PropertyValue value, PropertyId typeHint = default)
    {
        var s = value.Value;
        if (s != null)
            return s;
        if (typeHint == Database.PropType)
            return $"\"{_database.GetEntityTypeName((uint)value.IntValue)}\"";

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
    public string Print(IPredicate predicate)
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
                return $"{GetPropertyName(propertyEquals.Property)} {op} {Print(propertyEquals.Value, propertyEquals.Property)}";
            // case True @true:
            // break;
            default:
                throw new ArgumentOutOfRangeException(nameof(predicate));

        }
    }
    public void PrintChangeset(Changeset cs, bool oneLine = true)
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
            write("  " + change.ToString(_database));
        }
        if (oneLine)
            Console.WriteLine();
    }
}