using Moirai.Parser.Ast;

namespace Moirai.Parser;

/// Attributes: every `@name(...)` is checked against its declared parameters (StoryParser.Attributes) by
/// one binder, and only then handed to the attribute's own handler, which reads checked values instead
/// of poking at AST nodes. A mistake is an error with a position -- the alternative, which the old
/// naming convention and the old hand-rolled checks both had, is an attribute that quietly does nothing
/// or reads an argument that is not there.
public partial class AstVisitor
{
    /// The roles each role attribute declares, in argument order. What each argument must be is in the
    /// attribute's parameters: parents and partner must point at the annotated type itself, because a
    /// family tree reads them through that type's property ids.
    private static readonly Dictionary<string, EntityRole[]> RoleAttributes = new()
    {
        ["parents"] = [EntityRole.Parent1, EntityRole.Parent2],
        ["partner"] = [EntityRole.Partner],
        ["born"] = [EntityRole.Birth],
        ["died"] = [EntityRole.Death],
        ["alive"] = [EntityRole.Alive],
        ["dead"] = [EntityRole.Dead],
        ["period"] = [EntityRole.PeriodStart, EntityRole.PeriodEnd],
    };

    /// An attribute's arguments, checked: one value per argument, of the type its parameter's kind gives
    /// (see <see cref="BindAttribute"/>).
    private sealed class AttributeArgs(AttributeNode node, object?[] values)
    {
        public AttributeNode Node { get; } = node;
        public int Count => values.Length;
        public int Number(int i) => (int) values[i]!;
        public string Choice(int i) => (string) values[i]!;
        /// A string literal as written, quotes included.
        public string String(int i) => (string) values[i]!;
        public StringNode Text(int i) => (StringNode) values[i]!;
        public PropertyValue.ValueType EntityType(int i) => (PropertyValue.ValueType) values[i]!;
        public ExprNode Expr(int i) => Node.Args[i];
        public CallNode Query(int i) => (CallNode) values[i]!;
        public PropertyDefinition Property(int i) => (PropertyDefinition) values[i]!;
    }

    /// Checks <paramref name="attr"/> against <paramref name="descriptor"/>'s parameters: the number of
    /// arguments, then each argument's kind. Reports every mistake it finds and returns false if there was
    /// one. <paramref name="annotated"/> is the type a type attribute sits on, for Property arguments.
    ///
    /// Error codes are the ones these checks used before they were shared: too few arguments is
    /// MissingArgument, too many or a wrong kind InvalidArgument, an unknown property UnknownProperty.
    private bool BindAttribute(AttributeDescriptor descriptor, AttributeNode attr, EntityType? annotated,
        out AttributeArgs args)
    {
        var name = descriptor.Name;
        var values = new object?[attr.Args.Length];
        args = new AttributeArgs(attr, values);

        if (attr.Args.Length < descriptor.MinArgs)
        {
            AddError(StoryParser.ErrorCode.MissingArgument, attr.Span,
                $"@{name} expects {Arity(descriptor)}: {descriptor.Signature}");
            return false;
        }

        if (descriptor.MaxArgs is { } max && attr.Args.Length > max)
        {
            AddError(StoryParser.ErrorCode.InvalidArgument, attr.Span,
                $"@{name} expects {Arity(descriptor)}: {descriptor.Signature}");
            return false;
        }

        bool ok = true;
        for (int i = 0; i < attr.Args.Length; i++)
        {
            var param = descriptor.ParamAt(i)!;
            if (BindArgument(descriptor, param, attr.Args[i], annotated) is { } value)
                values[i] = value;
            else
                ok = false;
        }

        return ok;
    }

    static string Arity(AttributeDescriptor d) =>
        d.MaxArgs == 0 ? "no arguments"
        : d.MaxArgs == null ? $"at least {d.MinArgs} argument{(d.MinArgs == 1 ? "" : "s")}"
        : d.MinArgs == d.MaxArgs ? $"{d.MinArgs} argument{(d.MinArgs == 1 ? "" : "s")}"
        : $"{d.MinArgs} to {d.MaxArgs} arguments";

    /// The checked value of one argument, or null after reporting why it is not acceptable.
    private object? BindArgument(AttributeDescriptor descriptor, AttributeParam param, ExprNode arg, EntityType? annotated)
    {
        var name = descriptor.Name;
        var value = arg.Value;
        switch (param.Kind)
        {
            case AttributeArgKind.Number:
            {
                if (value?.Number is not { Kind: NumberKind.Int } number || !int.TryParse(number.Text, out var n))
                {
                    AddError(StoryParser.ErrorCode.InvalidArgument, arg.Span,
                        $"@{name}: {param.Name} must be a whole-number literal");
                    return null;
                }

                if (param.Min is { } min && n < min)
                {
                    AddError(StoryParser.ErrorCode.InvalidArgument, arg.Span, $"@{name}: {param.Name} must be at least {min}");
                    return null;
                }

                return n;
            }

            case AttributeArgKind.Choice:
            {
                // Bare (`PerXYear`) or qualified by its enum (`Frequency.PerXYear`).
                var member = value?.TypeId?.Text
                             ?? (value?.EnumValue is { } e && e.EnumType.Text == param.ChoiceEnum ? e.Member.Text : null);
                if (member == null || !param.Choices!.Contains(member))
                {
                    AddError(StoryParser.ErrorCode.UnknownEnum, arg.Span,
                        $"@{name}: {param.Name} must be one of {string.Join(", ", param.Choices!)}");
                    return null;
                }

                return member;
            }

            case AttributeArgKind.String:
                if (value?.StringLit is not { } plain || plain.Parts.Any(p => p is StringExprPart))
                {
                    AddError(StoryParser.ErrorCode.InvalidArgument, arg.Span,
                        $"@{name}: {param.Name} must be a string literal, with no {{...}} in it");
                    return null;
                }

                return GetText(arg.Span);

            case AttributeArgKind.Text:
                if (value?.StringLit is not { } text)
                {
                    AddError(StoryParser.ErrorCode.InvalidArgument, arg.Span, $"@{name}: {param.Name} must be a string literal");
                    return null;
                }

                return text;

            case AttributeArgKind.EntityType:
            {
                if (value?.TypeId is not { } typeId)
                {
                    AddError(StoryParser.ErrorCode.UnknownEntityType, arg.Span, "expected an Entity type");
                    return null;
                }

                var type = ParseType(typeId);
                if (!type.IsRefType)
                {
                    AddError(StoryParser.ErrorCode.UnknownEntityType, arg.Span, "expected an Entity type");
                    return null;
                }

                return type;
            }

            case AttributeArgKind.Query:
                // Parsed by the attribute's handler, which knows what $self is.
                if (value?.Call is not { FunId.Text: "each", DeclType: not null, VarId: not null, Scope: null, Else: null } query)
                {
                    AddError(StoryParser.ErrorCode.InvalidArgument, arg.Span,
                        $"@{name}: {param.Name} must be a query, each T $v: (predicate...), with no block");
                    return null;
                }

                return query;

            case AttributeArgKind.Property:
                return BindProperty(descriptor, param, arg, annotated!);

            default:
                throw new ArgumentOutOfRangeException(nameof(param.Kind), param.Kind, null);
        }
    }

    private PropertyDefinition? BindProperty(AttributeDescriptor descriptor, AttributeParam param, ExprNode arg,
        EntityType type)
    {
        var name = descriptor.Name;
        var propName = GetText(arg.Span).Trim();
        if (arg.Value?.Path == null || propName.Length == 0 || !propName.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            AddError(StoryParser.ErrorCode.InvalidArgument, arg.Span, $"@{name} expects a property name of {type.Name}");
            return null;
        }

        var def = type.Properties.FirstOrDefault(p => p.Name == propName);
        if (def.Name == null || !def.PropertyId.IsValid)
        {
            AddError(StoryParser.ErrorCode.UnknownProperty, arg.Span, $"{type.Name} has no property '{propName}'");
            return null;
        }

        Linker?.LinkProperty(new FileRange(arg.Span), def.PropertyId);

        var ok = !def.IsCollection && param.PropertyKind switch
        {
            PropertyKind.SelfReference => def.Type.BaseType == PropertyValue.ValueBaseType.Ref && def.Type.Index == type.Id.Id,
            PropertyKind.Number => def.Type.BaseType is PropertyValue.ValueBaseType.Number or PropertyValue.ValueBaseType.Float,
            _ => def.Type.BaseType == PropertyValue.ValueBaseType.Bool,
        };
        if (!ok)
        {
            var want = param.PropertyKind switch
            {
                PropertyKind.SelfReference => $"a {type.Name}",
                PropertyKind.Number => "a number",
                _ => "a bool",
            };
            AddError(StoryParser.ErrorCode.InvalidArgument, arg.Span, $"@{name} needs {want} property; '{propName}' is not one");
            return null;
        }

        return def;
    }

    /// Lowers a role attribute (@parents, @partner, @born, @died, @alive, @dead, @period) or @population
    /// onto its type. Returns false for any other attribute, which the @display pass handles.
    private bool VisitRoleAttribute(EntityType type, AttributeNode attr, ref bool populationDeclared)
    {
        var name = attr.Name.Text;
        if (name != "population" && !RoleAttributes.ContainsKey(name))
            return false;

        if (!BindAttribute(StoryParser.GetAttribute(name, AttributeTarget.Type)!, attr, type, out var args))
            return true;

        if (name == "population")
        {
            if (populationDeclared)
                AddError(StoryParser.ErrorCode.InvalidArgument, attr.Span, "only one type can be the @population");
            populationDeclared = true;
            type.IsPopulation = true;
            return true;
        }

        var other = name == "alive" ? EntityRole.Dead : name == "dead" ? EntityRole.Alive : (EntityRole?)null;
        if (other is { } o && type.IsDeclared(o))
        {
            AddError(StoryParser.ErrorCode.InvalidArgument, attr.Span, "@alive and @dead say the same thing; use one");
            return true;
        }

        var roles = RoleAttributes[name];
        for (int i = 0; i < roles.Length; i++)
            type.DeclareRole(roles[i], args.Property(i).PropertyId);
        return true;
    }

    /// `@display('Label', each T $v: (predicate...)[, 'item format'])`: a derived field on the type's details.
    private void VisitDisplayAttribute(EntityType type, AttributeNode attr)
    {
        // The old form named the type first and bound an implicit $other: say what to write instead.
        if (attr.Args is [{ Value.TypeId: { } oldType }, var label, var predicate, ..] && label.Value?.StringLit != null)
        {
            var format = attr.Args.Length > 3 ? ", " + GetText(attr.Args[3].Span) : "";
            AddError(StoryParser.ErrorCode.InvalidArgument, attr.Span,
                $"write @display({GetText(label.Span)}, each {oldType.Text} $other: ({GetText(predicate.Span)}){format}): " +
                "the items are a query, written as each is");
            return;
        }

        if (!BindAttribute(StoryParser.GetAttribute("display", AttributeTarget.Type)!, attr, type, out var args))
            return;

        var query = args.Query(1);
        using (new VariableDeclarationScopeDisposable(this, attr.Span))
        {
            DeclareVar("$self", type.RefType, attr.Name.Span, out var varIndex);
            var ctx = new FunctionParseContext(this, query, null);
            var itemVarIndex = ctx.ParseVariable(out var itemType, out _);
            IValueSql? expr = query.Args.Length == 0 ? new Literal(true) : ctx.ParsePredicateSql(itemType, query.Args.Length);
            if (expr == null)
                return;
            var itemDisplay = args.Count > 2 ? ParseInterpolatedString(args.Text(2)) : null;
            // The label as written, quotes included, which is how it has always reached the viewer.
            var d = new Display(Database.GetEntityType(itemType)!, varIndex, itemVarIndex, args.String(0), expr, itemDisplay);
            type.Attributes.Add(d);
        }
    }

    /// Lowers the attributes of the event or trigger being visited: its tags and its schedule.
    private void ParseAttributes(AttributeTarget target, out List<string>? tags, out IFilter? f)
    {
        tags = null;
        f = null;
        if (_currentAttribute == null) return;

        foreach (var p in _currentAttribute)
        {
            // StoryParser.Attributes is the registry: an attribute it does not list for this kind of
            // definition is unknown, which also stops a trigger's @frequency being silently ignored.
            if (StoryParser.GetAttribute(p.Name.Text, target) is not { } descriptor)
            {
                AddError(StoryParser.ErrorCode.UnknownCall, p.Span, "Unknown attribute");
                continue;
            }

            if (!BindAttribute(descriptor, p, null, out var args))
                continue;

            switch (descriptor.Name)
            {
                case "tag":
                    tags ??= new();
                    // The literal as written, quotes included: a quirk as old as the ANTLR port, preserved.
                    for (int i = 0; i < args.Count; i++)
                        tags.Add(args.String(i));
                    break;
                case "start":
                    f = new FilterAtStart();
                    break;
                case "frequency":
                    int x = args.Number(0), y = args.Number(2);
                    f = Enum.Parse<Database.Frequency>(args.Choice(1)) switch
                    {
                        Database.Frequency.EveryXYear => new FilterExactlyXEveryYYears(x, y, Database.Actions.Count + 1),
                        Database.Frequency.PerXYear => new FilterProbabilityXPerYears(x, y),
                        _ => throw new ArgumentOutOfRangeException(),
                    };
                    break;
                default:
                    throw new InvalidOperationException($"@{descriptor.Name} is registered but has no handler");
            }
        }
    }
}
