using System.Diagnostics;
using System.Globalization;
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
        foreach (EnumDefinition en in _database.Enums.Skip(Database.BuiltinEnumCount))
        {
            sb.AppendLine($"enum {en.Name} {{ {string.Join(", ", en.Values)} }}");

        }
        foreach (var property in _database.Properties.Skip(Database.DefaultProperties().Count))
        {
            sb.AppendLine($"prop {property.Name}: {Print(property.Type)}");
        }
        foreach (var action in _database.Actions.Concat(_database.Triggers))
        {
            if (action.Filter != null)
                sb.AppendLine(Print(action.Filter));
            sb.AppendLine($"{(action.IsTrigger ? "trigger" : "event")} {action.Name}{string.Join("", action.Categories.Select(t => $" {_database.GetCategoryName(t)}"))} {{");
            if(action.IsTrigger)
                sb.AppendLine($"  when{(action.When.Item1 == EventTrigger.WhenType.Created ? "_created" : "")} {Print(action.When.Item2)}{(action.When.Item3 == null ? "" : (" and " + Print(action.When.Item3)))}");
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

            case PropertyValue.ValueBaseType.Ref:
                return propertyType.Index == 0
                    ? propertyType.BaseType.ToString().ToLowerInvariant()
                    : _database.GetEntityType(propertyType).Name;
            case PropertyValue.ValueBaseType.None:
            case PropertyValue.ValueBaseType.String:
            case PropertyValue.ValueBaseType.Number:
            case PropertyValue.ValueBaseType.Float:
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
        string indentStr = MakeIndent(indent);
        switch (instruction)
        {
            case CallInstruction call:
                
                sb.Append(Print(call.Value, indent) + Environment.NewLine);
                break;
            case SetProperty setProperty:
                sb.AppendLine(
                    $"{indentStr}{(setProperty.IsLocalVar ? "var" :"set")} {Print(setProperty.PropertySet)}{(setProperty.IsLocalVar ? ":" : " =")} {Print(setProperty.Parameter)}");
                break;
            
            default:
                throw new ArgumentOutOfRangeException(nameof(instruction), $"instr: '{instruction}'");
        }
    }

    private static string MakeIndent(int indent)
    {
        return new string(' ', indent * 4);
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
            {
                var e = _database.Enums[value.Type.Index];
                if (value.IntValue == 0) return "null";
                return (storyMode & History.HistoryMode.Story) != 0
                    ? e.FormattedValues[(int)value.IntValue - 1]
                    : $"{e.Name}.{e.Values[(int)value.IntValue - 1]}";
            }
            case PropertyValue.ValueBaseType.EnumType:
            {
                var e = _database.Enums[value.Type.Index];
                return e.Name;
            }
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
            case PropertyValue.ValueBaseType.Float:
                return value.FloatValue.ToString(CultureInfo.InvariantCulture);
            case PropertyValue.ValueBaseType.Bool:
                return value.BoolValue ? "true" : "false";
            case PropertyValue.ValueBaseType.EntityType:
                return _database.GetEntityTypeName(value.TypeId);
            default:
                throw new ArgumentOutOfRangeException();
        }
        return value.IntValue.ToString();
    }
    
    public string Print(IValue value, int indent = 0)
    {
        string indentStr = indent == 0 ? String.Empty : new string(' ', indent * 4);
        if (value is IValueCall call)
        {
            return indentStr + call.Print(this);
        }
        StringBuilder sb = new StringBuilder();
        
        switch (value)
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
            case Record formatAction:
                sb.AppendLine($"{indentStr}record {Print(formatAction.String)}");
                break;
            case CallRule callRule:
                sb.AppendLine( $"{indentStr}callRule " + _database.Actions[callRule.RuleIndex].Name);
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
            case If @if:
                sb.AppendLine($"{indentStr}if {Print(@if.Condition)} {{");
                foreach (var nestedEffect in @if.IfTrue)
                {
                    PrintEffect(nestedEffect, sb, indent + 1);
                }
                if (@if.IfFalse.Length > 0)
                {
                    sb.AppendLine($"{indentStr}}} else {{");
                    foreach (var nestedEffect in @if.IfFalse)
                    {
                        PrintEffect(nestedEffect, sb, indent + 1);
                    }
                    
                }
                sb.AppendLine($"{indentStr}}}");

                break;
            case Match match:
            {
                sb.AppendLine($"{indentStr}match {String.Join(", ", match.Values.Select(Print))} {{");
                var caseIndent = MakeIndent(indent + 1);
                foreach (var matchCase in match.Cases)
                {
                    sb.Append($"{caseIndent}{String.Join(", ", matchCase.Item1.Select(Print))} => ");
                    if (matchCase.Item2.Length == 0)
                        sb.AppendLine("{ }");
                    else
                    {
                        sb.AppendLine("{");
                        foreach (var instruction1 in matchCase.Item2)
                        {
                            PrintEffect(instruction1, sb, indent + 2);
                        }

                        sb.AppendLine($"{caseIndent}}}");
                    }
                }

                sb.AppendLine($"{indentStr}}}");
                break;
            }
            case MatchWeight match:
            {
                sb.AppendLine($"{indentStr}random_weighted {Print(match.Value)} {{");
                var caseIndent = MakeIndent(indent + 1);
                int accWeight = 0;
                foreach (var matchCase in match.CumulativeWeights)
                {
                    
                    var w = matchCase.Item1 == -1 ? -1 : (matchCase.Item1 - accWeight);
                    accWeight = matchCase.Item1;
                    sb.Append($"{caseIndent}{(w == -1 ? "_" : w.ToString())} => ");
                    if (matchCase.Item2.Length == 0)
                        sb.AppendLine("{ }");
                    else
                    {
                        sb.AppendLine("{");
                        foreach (var instruction1 in matchCase.Item2)
                        {
                            PrintEffect(instruction1, sb, indent + 2);
                        }

                        sb.AppendLine($"{caseIndent}}}");
                    }
                }
                sb.AppendLine($"{indentStr}}}");

                break;
            }
            case InterpolatedString interpolatedString:
                return
                    $"'{string.Format(interpolatedString.FormatString, interpolatedString.Arguments.Select(a => (object)($"{{{Print(a)}}}")).ToArray())}'";
            case Literal literal:
                return Print(literal.Value);
            case PropertyPath path:
                return Print(path);
            case RandomEnum rnd:
                return "random " + _database.Enums[rnd.EnumID.Id].Name;
           
            case And and:
                return string.Join(", ", and.Predicates.Select(Print));
            
            case BinaryOperator propertyEquals:
                string op = propertyEquals.Op switch
                {

                    BinaryOperator.Operator.And => "and",
                    BinaryOperator.Operator.Or => "or",
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
            case MatchAnyValue _: return "_";
            // case True @true:
            // break;
            default:
                return "// !!!!!!!!!!!";
                // throw new ArgumentOutOfRangeException(nameof(value) + ":" + value);

        }

        return sb.ToString();
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
        // TODO CS
        // foreach (var change in cs.Changes)
        // {
            // write("  " + change.ToString(_database));
        // }
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
    public void PrintDb()
    {
        // Console.WriteLine("[DB]");
        bool any = false;
        foreach (var e in _database.Entities)
        {
            any = true;
            this.PrintEntity(e);
        }
        if (!any)
            Console.WriteLine("<Empty>");
        Console.WriteLine();
    }
    public void PrintHistory()
    {
        foreach (var cs in _database.History.Changesets)
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

    public string GetRuleName(int eventIndex) => _database.Actions[eventIndex].Name;

    public void PrintMarked()
    {
        foreach (var ((eid, index), year) in _database.Ctx._marked)
        {
            Console.WriteLine($"{eid,6}{GetRuleName(index-1),20} : {year}");
        }
    }

    public void PrintRecords()
    {
        foreach (var record in _database.Records)
        {
            Console.WriteLine($"{record.Year,4} {record.Text}");
        }
    }
}
