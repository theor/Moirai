using System.Text;
using Moirai.Core;

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
        foreach (EntityType type in _database.Types.Skip(_database.BuiltinTypes))
        {
            sb.AppendLine($"entity {type.Name} {{ }}");

        }
        foreach (string en in _database.Tags.Skip(1))
        {
            sb.AppendLine($"tag {en}");

        }
        foreach (EnumDefinition en in _database.Enums.Skip(1))
        {
            sb.AppendLine($"enum {en.Name} {{ {string.Join(", ", en.Values)} }}");

        }
        foreach (var property in _database.Properties.Skip(Database.DefaultProperties().Count))
        {
            sb.AppendLine($"prop {property.Name}: {Print(property.Type)}");
        }
        foreach (var action in _database.Actions.Concat(_database.Events))
        {
            if (action.Filter != null)
                sb.AppendLine(Print(action.Filter));
            sb.AppendLine($"{(action.IsEvent ? "event" : "rule")} {action.Name}{string.Join("", action.Categories.Select(t => $" {_database.GetCategoryName(t)}"))} {{");
            foreach (var when in action.WhenTags) sb.AppendLine($"  when {_database.GetTagName(when)}");
            foreach (var when in action.Whens) sb.AppendLine($"  when ${when.VariableIndex}: {Print(when.Value)}");
            foreach (var effect in action.Effects)
            {
                PrintEffect(effect, sb, 1);
            }
            sb.AppendLine("}");

        }
        return sb.ToString();
    }
    private string Print(IFilter actionFilter)
    {
        switch (actionFilter)
        {
            case FilterAtStart:
               return "@start";
            case FilterExactlyXEveryYYears filterExactlyXEveryYYears:
               return $"@ {filterExactlyXEveryYYears.Count} every {filterExactlyXEveryYYears.Years} years";
            case FilterProbabilityXPerYears filterProbabilityXPerYears:
                return $"@ {filterProbabilityXPerYears.Event.ExpectedOccurences} every {filterProbabilityXPerYears.Event.ExpectedInterval} years";
            default:
                throw new ArgumentOutOfRangeException(nameof(actionFilter));

        }
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
    public string PrintEffect(IInstruction instruction)
    {
        StringBuilder sb = new();
        PrintEffect(instruction, sb, 0);
        return sb.ToString();
    }
    private void PrintEffect(IInstruction instruction, StringBuilder sb, int indent)
    {
        string indentStr = new string(' ', indent * 4);
        switch (instruction)
        {
            case CreateEntity createEntity:
                sb.AppendLine($"{indentStr}create ${createEntity.VariableIndex}: {_database.GetEntityTypeName(createEntity.Type)}, {Print(createEntity.Name)}");
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
                    $"{indentStr}{(setProperty.IsLocalVar ? "var" :"set")} {Print(setProperty.PropertySet)} = {Print(setProperty.Parameter)}");
                break;
            case FormatAction formatAction:
                sb.AppendLine($"{indentStr}record {Print(formatAction.String)}");
                break;
            case CallRule call:
                sb.AppendLine( $"{indentStr}call ${call.VariableIndex}: " + _database.Actions[call.RuleIndex].Name);
                break;
            case TagEntity tag:
                sb.AppendLine($"{indentStr}add_tag {Print(tag.Path)}, {_database.GetTagName(tag.TagId)}");
                break;
            case AssertInstr assert:
                switch (assert.Mode)
                {

                    case AssertInstr.AssertMode.True:
                        sb.AppendLine($"{indentStr}assert " + Print(assert.Value));
                        break;
                    case AssertInstr.AssertMode.Eq:
                        sb.AppendLine($"{indentStr}assert_eq {Print(assert.Value)}, {Print(assert.Right!)}");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(instruction), $"instr: '{instruction}'");
        }
    }
    private string GetPropertyName(PropertyId p)
    {
        if (p.IsValid && p.Id < _database.Properties.Count)
            return _database.Properties[(int)p.Id].Name;

        return "<??>";
    }
    private string Print(PropertyPath path)
    {
        if(path.Mode == PropertyPath.PropertyPathMode.Singleton)
            return $"#{_database.GetEntityTypeName(path.SingletonType)}.{GetPropertyName(path.Property)}";
        if (path.VariableIndex == -1)
            return GetPropertyName(path.Property);
        return path.Property != PropertyId.Null ? $"${path.VariableIndex}.{GetPropertyName(path.Property)}" : $"${path.VariableIndex}";
    }

    public string Print(PropertyValue value, History.HistoryMode storyMode = History.HistoryMode.Default)
    {
        var s = value.Value;
        if (s != null)
            return s;
        

        switch (value.Type.BaseType)
        {
            case PropertyValue.ValueBaseType.Enum:
                var e = _database.Enums[value.Type.Index];
                if (value.IntValue == 0) return "null";
                return (storyMode & History.HistoryMode.Story) != 0 ? e.FormattedValues[(int)value.IntValue-1] : $"{e.Name}.{e.Values[(int)value.IntValue-1]}";
            case PropertyValue.ValueBaseType.None:
                return "null";
            case PropertyValue.ValueBaseType.String:
                return $"'{value.Value}'";
            case PropertyValue.ValueBaseType.Ref:
                if (value.IntValue == 0)
                    return "null";
                // if ((storyMode & History.HistoryMode.FormatEntityIds) != 0)
                //     return $"%id{value.Id.Id}%";
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
            case InterpolatedString interpolatedString:
                return
                    $"'{string.Format(interpolatedString.FormatString, interpolatedString.Arguments.Select(a => (object)($"{{{Print(a)}}}")).ToArray())}'";
            case Literal literal:
                return Print(literal.Value);
            case PropertyPath path:
                return Print(path);
            case RandomCall rnd:
                return "random " + _database.Enums[rnd.EnumID].Name;
            case RandomName rnd:
                return "random " + rnd.Type.ToString().ToLowerInvariant();
           
            case And and:
                return string.Join(", ", and.Predicates.Select(Print));
            
            case BinaryOperator propertyEquals:
                string op = propertyEquals.Op switch
                {

                    BinaryOperator.Operator.Equals => "=",
                    BinaryOperator.Operator.NotEquals => "!=",
                    BinaryOperator.Operator.Add => "+",
                    BinaryOperator.Operator.Sub => "-",
                    BinaryOperator.Operator.Div => "/",
                    BinaryOperator.Operator.Mul => "*",
                    BinaryOperator.Operator.Gt => ">",
                    BinaryOperator.Operator.Ge => ">=",
                    BinaryOperator.Operator.Lt => "<",
                    BinaryOperator.Operator.Le => "<=",
                    _ => throw new ArgumentOutOfRangeException()
                };
                return $"({Print(propertyEquals.Left)} {op} {Print(propertyEquals.Right)})";
            case IsOfType ofType :
                return $"({Print(ofType.Entity)} = {Print(ofType.ValueTypeId)})";
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
    public void PrintEntity(Entity e)
    {

        var type = _database.GetEntityTypeName(e.GetProperty(Database.PropType).TypeId);
        string name = e.TryGetProperty(Database.PropName, out var nameprop) ? (nameprop.Value ?? "") : "";
        Console.ForegroundColor = Colors[e.GetProperty(Database.PropType).IntValue % Colors.Length];
        Console.WriteLine($"{e.Id} {type} {name}");
        Console.ResetColor();
        if (e.Properties != null)
            foreach (var property in e.Properties)
            {
                if (property.Id == Database.PropType || property.Id == Database.PropName || !property.Id.IsValid)
                    continue;

                Console.Write($"  {_database.Properties[(int)property.Id.Id].Name}: {Print(property.Value)}");
                if (property.Value.Type == PropertyValue.TypeRef && _database.TryGetEntity(property.Value.Id, out var other) &&
                    other.TryGetProperty(Database.PropName, out var otherName))
                    Console.Write(" " + otherName.Value);
                Console.WriteLine();
            }
    }
    private static readonly ConsoleColor[] Colors = { ConsoleColor.Cyan, ConsoleColor.Magenta, ConsoleColor.Green, ConsoleColor.Yellow };
    public void PrintDb(Database database)
    {
        // Console.WriteLine("[DB]");
        bool any = false;
        foreach (var e in database.Entities)
        {
            any = true;
            this.PrintEntity(e);
        }
        if (!any)
            Console.WriteLine("<Empty>");
        Console.WriteLine();
    }
    public void PrintHistory(Database database)
    {
        foreach (var cs in database.History.Changesets)
        {
            this.PrintChangeset(cs);
        }
    }
    public string Format(InterpolatedString formatAction, Database database, bool injectIdTags = false)
    {
        var propertyValues = formatAction.Arguments.Select(v =>
        {

            var print = Print(v.Compute(database.Ctx), History.HistoryMode.Story);
            if (injectIdTags && v is PropertyPath path && path.Mode == PropertyPath.PropertyPathMode.Variable)
            {
                var entity = database.Ctx.Argument(path.VariableIndex);
                if(entity.Type == PropertyValue.TypeRef)
                    return $"<{entity.Id}>{print}</>";
            }
            return print;
        }).Cast<object?>().ToArray();
        return String.Format(formatAction.FormatString, propertyValues);
    }
}
