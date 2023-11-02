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
        foreach (EnumDefinition en in _database.Enums.Skip(1))
        {
            sb.AppendLine($"enum {en.Name} {{ {string.Join(", ", en.Values)} }}");

        }
        foreach (var property in _database.Properties.Skip(Database.DefaultProperties().Count))
        {
            // TODO types
            sb.AppendLine($"prop {property.Name} = {Print(property.Type)}");
        }
        foreach (var action in _database.Actions.Concat(_database.Events))
        {
            sb.AppendLine($"{(action.IsEvent ? "event" : "rule")} {action.Name} {{");
            foreach (var when in action.Whens)
            {
                sb.AppendLine($"  when ${when.VariableIndex}: {Print(when.Value)}");

            }
            foreach (var effect in action.Effects)
            {
                PrintEffect(effect, sb, 1);
            }
            sb.AppendLine("}");

        }
        return sb.ToString();
    }
    private string Print(PropertyValue.ValueType propertyType)
    {
        switch (propertyType.BaseType)
        {

            case PropertyValue.ValueBaseType.None:
            case PropertyValue.ValueBaseType.String:
            case PropertyValue.ValueBaseType.Ref:
            case PropertyValue.ValueBaseType.Number:
            case PropertyValue.ValueBaseType.Bool:
                return propertyType.BaseType.ToString().ToLowerInvariant();
            case PropertyValue.ValueBaseType.Enum:
                return _database.Enums[propertyType.Index].Name;
            case PropertyValue.ValueBaseType.EntityType:
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    private void PrintEffect(IInstruction instruction, StringBuilder sb, int indent)
    {
        string indentStr = new string(' ', indent * 2);
        switch (instruction)
        {
            case CreateEntity createEntity:
                sb.AppendLine($"{indentStr}create ${createEntity.VariableIndex}: {_database.GetEntityTypeName(createEntity.Type)}");
                break;
            // case NameEntity nameEntity:
            case AssignPick predicateParameter:
                sb.Append(
                    $"{indentStr}{predicateParameter.CallType.ToString().ToLowerInvariant()} ${predicateParameter.VariableIndex}: {Print(predicateParameter.Value)}");
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
                throw new ArgumentOutOfRangeException(nameof(instruction));
        }
    }
    private string GetPropertyName(PropertyId p)
    {
        if (p.IsValid && p.Id < _database.Properties.Count)
            return _database.Properties[(int)p.Id].Name;

        return "<??>";
    }
    private string Print(PropertyPath path) =>
        path.Property != PropertyId.Null ? $"${path.VariableIndex}.{GetPropertyName(path.Property)}" : $"${path.VariableIndex}";
   
    public string Print(PropertyValue value, PropertyId typeHint = default)
    {
        var s = value.Value;
        if (s != null)
            return s;
        if (typeHint == Database.PropType)
            return $"\"{_database.GetEntityTypeName(value.TypeId)}\"";

        switch (value.Type.BaseType)
        {
            case PropertyValue.ValueBaseType.Enum:
                var e = _database.Enums[value.Type.Index];
                return $"{e.Name}.{e.Values[(int)value.IntValue]}";
            case PropertyValue.ValueBaseType.None:
                return "null";
            case PropertyValue.ValueBaseType.String:
                return $"\"{value.Value}\"";
            case PropertyValue.ValueBaseType.Ref:
                if (value.IntValue == 0)
                    return "null";

                return "#" + value.IntValue;
            case PropertyValue.ValueBaseType.Number:
                return value.IntValue.ToString();
            case PropertyValue.ValueBaseType.Bool:
                return value.BoolValue ? "true" : "false";
            case PropertyValue.ValueBaseType.EntityType:
                return _database.GetEntityTypeName(value.TypeId);
            default:
                throw new ArgumentOutOfRangeException();
        }
        return value.IntValue.ToString();
    }
    
    public string Print(IValue value)
    {
        switch (value)
        {
            case Literal literal:
                return Print(literal.Value);
            case PropertyPath path:
                return Print(path);
            case RandomCall rnd:
                return "random " + _database.Enums[rnd.EnumID].Name;
            case YearsPassed y:
                return "(passed_years " + y.Years + ")";
           
            case And and:
                return string.Join(", ", and.Predicates.Select(Print));
            
            case BinaryOperator propertyEquals:
                string op = propertyEquals.Op switch
                {

                    BinaryOperator.Operator.Equals => "=",
                    BinaryOperator.Operator.NotEquals => "!=",
                    _ => throw new ArgumentOutOfRangeException()
                };
                return $"{Print(propertyEquals.Left)} {op} {Print(propertyEquals.Right)}";
            // case True @true:
            // break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value) + ":" + value);

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
