using Moirai.Parser.Ast;
using Superpower.Model;

namespace Moirai.Parser;

public class AstVisitor : StoryParser.IVisitor
{
    public record struct VariableDeclaration(string Name, PropertyValue.ValueType Type, FileRange DeclarationRange)
    {
    }

    public (int offsetLine, int offsetColumn) Offset { get; set; }

    public readonly Parser.VariableDeclarationScope RootScope;
    private Parser.VariableDeclarationScope _current;

    public List<StoryParser.Error> Errors { get; } = new();

    public StoryParser.ILinker? Linker { get; set; }

    public readonly Database Database;

    public AstVisitor(Database database)
    {
        Database = database;
        RootScope = new(null, FileRange.Empty);
        _current = RootScope;
    }

    AttributeNode[] _currentAttribute;

    public void VisitR(RNode context)
    {
        // enum_definition first (types/tables may reference enums).
        foreach (var def in context.Defs)
            if (def.EnumDefinition != null)
                VisitEnumDefinition(def.EnumDefinition);

        List<(EntityType Id, TypeDefinitionNode Node)> typesContexts = new();
        List<(EntityType Id, AttributeNode Attr)> deferredTypeAttributes = new();

        foreach (var def in context.Defs)
        {
            if (def.TypeDefinition is not { } typeDefinitionContext) continue;

            if (typeDefinitionContext.TypeName == null || typeDefinitionContext.IsLowercaseName)
            {
                AddError(StoryParser.ErrorCode.TypenameMustStartWithUpperCase, typeDefinitionContext.Span,
                    GetText(typeDefinitionContext.Span));
                continue;
            }

            string typeName = typeDefinitionContext.TypeName.Value.Text;
            EntityType type = DeclareEntityType(typeName);
            type.IsSingleton = typeDefinitionContext.IsSingleton;

            Linker?.DeclareType(typeDefinitionContext.Span, type.Id);
            Linker?.LinkType(new FileRange(typeDefinitionContext.TypeName.Value.Span), type.Id, isDeclaration: true);

            typesContexts.Add((type, typeDefinitionContext));
            foreach (var attr in def.Attributes)
                deferredTypeAttributes.Add((type, attr));
        }

        foreach (var (type, typeDefinitionContext) in typesContexts)
        {
            foreach (var propDefinitionContext in typeDefinitionContext.PropDefinitions)
            {
                var propName = propDefinitionContext.PropertyId.Text;
                if (type.GetPropertyId(propName).Id != 0)
                {
                    AddError(StoryParser.ErrorCode.DuplicatePropertyDefinition, typeDefinitionContext.Span, propName);
                    continue;
                }

                bool isCollection = propDefinitionContext.Type.IsCollection;
                PropertyValue.ValueType proptype = ParseType(propDefinitionContext.Type.Name);
                var propertyDefinition = new PropertyDefinition(propName, type.Id, (uint) type.Properties.Count,
                    proptype, isCollection);
                type.Properties.Add(propertyDefinition);
                Linker?.DeclareTypeProperty(propDefinitionContext.Span, propertyDefinition.PropertyId);
                Linker?.LinkProperty(new FileRange(propDefinitionContext.PropertyId.Span),
                    propertyDefinition.PropertyId, isDeclaration: true);
            }

            foreach (var functionDefinitionContext in typeDefinitionContext.FunctionDefinitions)
                ParseFunctionDefinition(functionDefinitionContext, type);
        }

        // Tables can reference enums and entity types, so register them after both are declared
        // but before functions/events (whose bodies may call roll(...)).
        foreach (var def in context.Defs)
            if (def.TableDefinition != null)
                VisitTableDefinition(def.TableDefinition);

        foreach (var (tid, attr) in deferredTypeAttributes)
        {
            if (attr.Name.Text != "display")
            {
                AddError(StoryParser.ErrorCode.UnknownAttribute, attr.Name.Span, attr.Name.Text);
                continue;
            }

            if (attr.Args.Length < 3)
            {
                AddError(StoryParser.ErrorCode.MissingArgument, attr.Span,
                    "display expects two arguments, a string and and expression");
                continue;
            }

            var refTypeIdent = attr.Args[0].Value?.TypeId;
            if (refTypeIdent == null)
            {
                AddError(StoryParser.ErrorCode.UnknownEntityType, attr.Args[0].Span, "expected an Entity type");
                continue;
            }

            var refReferencedType = ParseType(refTypeIdent.Value);
            if (!refReferencedType.IsRefType)
                AddError(StoryParser.ErrorCode.UnknownEntityType, attr.Span, "expected an Entity type");

            using (new VariableDeclarationScopeDisposable(this, attr.Span))
            {
                DeclareVar("$self", tid.RefType, attr.Name.Span, out var varIndex);
                DeclareVar("$other", refReferencedType, attr.Name.Span, out var otherVarIndex);
                var expr = ParseExprSql(attr.Args[2])!;
                InterpolatedString? itemDisplay = null;
                if (attr.Args.Length > 3 && attr.Args[3].Value?.StringLit != null)
                    itemDisplay = ParseInterpolatedString(attr.Args[3].Value!.StringLit);
                Display d = new Display(Database.GetEntityType(refReferencedType)!, varIndex, otherVarIndex,
                    GetText(attr.Args[1].Span), expr, itemDisplay);
                var t = Database.Types[(int) (tid.Id.Id)];
                t.Attributes.Add(d);
            }
        }

        foreach (var def in context.Defs)
            if (def.FunctionDefinition != null)
                ParseFunctionDefinition(def.FunctionDefinition);

        foreach (var def in context.Defs)
        {
            _currentAttribute = def.Attributes;
            if (def.Event != null)
                VisitEvent(def.Event);
            else if (def.Trigger != null)
                VisitTrigger(def.Trigger);
            _currentAttribute = null!;
        }
    }

    private void ParseFunctionDefinition(FunctionDefinitionNode fundef, EntityType? instanceType = null)
    {
        using var _ = new VariableDeclarationScopeDisposable(this, fundef.Scope.Span);
        var rootScope = _current;

        var name = fundef.Name.Text;
        PropertyValue.ValueType returnType = PropertyValue.ValueType.Null;
        if (fundef.ReturnType != null)
            returnType = ParseType(fundef.ReturnType.Name);

        if (instanceType != null)
            DeclareVar("$self", instanceType.RefType, fundef.Name.Span, out var selfVarIndexUnused);

        var parameters = fundef.Params.Select(p =>
        {
            var paramName = p.VarId.Text;
            var paramType = ParseType(p.Type.Name);
            DeclareVar(paramName, paramType, p.VarId.Span, out var paramIndex);
            return new FunctionDefinition.Parameter(paramName, paramType, paramIndex);
        }).ToArray();
        var functionDefinitionId = new FunctionDefinitionId(
            (ushort) (instanceType == null ? Database.Functions.Count : instanceType.Functions.Count));
        var instructions = ParseScope(fundef.Scope, out var actualType);
        var functionDefinition = new FunctionDefinition(functionDefinitionId,
            name,
            instanceType?.Id ?? EntityTypeId.Null,
            returnType,
            parameters,
            instructions,
            ConvertScope(rootScope));
        if (instanceType != null)
            instanceType.Functions.Add(functionDefinition);
        Database.Functions.Add(functionDefinition);
        // A function with no declared return type is a procedure: its body is effects (create/set/
        // record/call) and any trailing value is ignored, so only value functions check the body type.
        if (returnType != PropertyValue.ValueType.Null && actualType != returnType)
            AddError(
                actualType == PropertyValue.ValueType.Null
                    ? StoryParser.ErrorCode.MissingReturnValue
                    : StoryParser.ErrorCode.MismatchedReturnType, fundef.Span, $"{actualType} != {returnType}");

        Linker?.DeclareFunction(new FileRange(fundef.Name.Span), new UserFunctionDescriptor(functionDefinition));
    }

    public EntityType DeclareEntityType(string typeName)
    {
        var id = (uint) Database.Types.Count;
        var entityType = new EntityType(typeName, id);
        Database.Types.Add(entityType);
        return entityType;
    }

    public PropertyValue.ValueType ParseType(Ident id)
    {
        switch (id.Text)
        {
            case "bool": return PropertyValue.TypeBool;
            case "number": return PropertyValue.TypeNumber;
            case "float": return PropertyValue.TypeFloat;
            case "string": return PropertyValue.TypeString;
            case "percentage": return PropertyValue.TypePercent;
            default:
                if (Database.GetEnumDefinition(id.Text, out EnumDefinition enumDefinition))
                {
                    Linker?.LinkEnum(new FileRange(id.Span), enumDefinition.Index);
                    return PropertyValue.TypeEnum(enumDefinition.Index);
                }

                var entityType = Database.GetEntityType(id.Text);
                Linker?.LinkType(new FileRange(id.Span), entityType.Id);
                if (entityType.Id.IsValid)
                    return entityType.RefType;
                AddError(StoryParser.ErrorCode.UnknownPropertyType, id.Span, id.Text);
                return default;
        }
    }

    private void VisitEnumDefinition(EnumDefinitionNode context)
    {
        EnumDefinition en = new(new EnumDefinitionId((ushort) Database.Enums.Count), context.Name.Text,
            context.Members.Select(v => v.Text).ToList());
        Database.Enums.Add(en);
        Linker?.DeclareEnum(context.Span, en.Index);
        Linker?.LinkEnum(new FileRange(context.Name.Span), en.Index, isDeclaration: true);
        foreach (var memberNode in context.Members)
            if (en.GetValueFromName(memberNode.Text, out var memberValue))
                Linker?.LinkEnumMember(new FileRange(memberNode.Span), memberValue, isDeclaration: true);
    }

    private void VisitTableDefinition(TableDefinitionNode context)
    {
        var name = context.Name.Text;
        if (Database.GetTableDefinition(name, out _))
        {
            AddError(StoryParser.ErrorCode.DuplicateDefinition, context.Span, name);
            return;
        }

        var entries = context.Entries;
        var weighted = new (int, IValue)[entries.Length];
        PropertyValue.ValueType valueType = default;
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            int weight = entry.Weight ?? 1;
            if (weight < 0) weight = 0;
            var val = ParseValue(entry.Value, out var t);
            if (i == 0) valueType = t;
            weighted[i] = (weight, val);
        }

        Database.Tables.Add(new Moirai.Core.TableDefinition(Database.Tables.Count, name, weighted, valueType));
    }

    private void VisitEvent(EventNode context)
    {
        string actionId = context.Name.Text;
        using var _ = new VariableDeclarationScopeDisposable(this, context.Scope.Span);
        var rootScope = _current;

        ParseAttributes(out var tags, out var f);

        // Event parameters occupy the scope's first value-stack slots (0..n-1); call(name, args...)
        // writes them before the body runs (see CallRule).
        CurrentEventTrigger = new EventTrigger(Database.Actions.Count + 1, actionId, false, f, tags: tags);

        if (context.Params.Length > 0)
        {
            var ps = new List<FunctionDefinition.Parameter>(context.Params.Length);
            foreach (var p in context.Params)
            {
                var paramName = p.VarId.Text;
                var paramType = ParseType(p.Type.Name);
                DeclareVar(paramName, paramType, p.VarId.Span, out var paramIndex);
                ps.Add(new FunctionDefinition.Parameter(paramName, paramType, paramIndex));
            }

            CurrentEventTrigger.Parameters = ps;
        }

        foreach (var effectContext in context.Scope.Effects)
        {
            var effect = ParseEffect(effectContext, out var unusedEffectType);
            CurrentEventTrigger.Effects.Add(effect);
        }

        CurrentEventTrigger.DebugScopeRoot = ConvertScope(rootScope);
        Database.Actions.Add(CurrentEventTrigger);
        CurrentEventTrigger = null;
    }

    private void ParseAttributes(out List<string>? tags, out IFilter? f)
    {
        tags = null;
        f = null;
        if (_currentAttribute == null) return;

        foreach (var p in _currentAttribute)
        {
            switch (p.Name.Text)
            {
                case "tag":
                    tags ??= new();
                    foreach (var arg in p.Args)
                        // GetText (not GetString): the original ANTLR port used the string literal's raw
                        // text including its surrounding quotes here (a pre-existing quirk, not something
                        // introduced by this migration -- preserved as-is; see call()'s use of GetString
                        // for the deliberately-trimmed counterpart).
                        tags.Add(GetText(arg.Span));
                    break;
                case "start":
                    f = new FilterAtStart();
                    break;
                case "frequency":
                    if (p.Args.Length != 3)
                        AddError(StoryParser.ErrorCode.MissingArgument, p.Span, "frequency expects 3 arguments");

                    if (!ParseEnum<Database.Frequency>(p.Args[1].Value!, out var val))
                        AddError(StoryParser.ErrorCode.UnknownEnum, p.Args[1].Span,
                            "Should be a value among " + string.Join(", ", Enum.GetNames<Database.Frequency>()));
                    var x = int.Parse(GetText(p.Args[0].Span));
                    var y = int.Parse(GetText(p.Args[2].Span));
                    switch (val)
                    {
                        case Database.Frequency.EveryXYear:
                            f = new FilterExactlyXEveryYYears(x, y, Database.Actions.Count + 1);
                            break;
                        case Database.Frequency.PerXYear:
                            f = new FilterProbabilityXPerYears(x, y);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;
                default:
                    AddError(StoryParser.ErrorCode.UnknownCall, p.Span, "Unknown attribute");
                    break;
            }
        }
    }

    public EventTrigger? CurrentEventTrigger;

    private void VisitTrigger(TriggerNode context)
    {
        string actionId = context.Name.Text;
        ParseAttributes(out var tags, out _);
        CurrentEventTrigger = new EventTrigger(Database.Triggers.Count + 1, actionId, true, null, tags: tags);

        using var scopeDisposable = new VariableDeclarationScopeDisposable(this, context.Scope.Span);
        var rootScope = _current;
        if (context.Scope.WhenCreated is { } createdContext)
        {
            EntityType type = Database.GetEntityType(createdContext.TypeId.Text);
            if (!type.Id.IsValid)
                AddError(StoryParser.ErrorCode.UnknownPropertyType, createdContext.TypeId.Span,
                    createdContext.TypeId.Text);

            DeclareVar("$new", type.RefType, createdContext.Keyword.Span, out _);
            CurrentEventTrigger.When = (EventTrigger.WhenType.Created, type.Id, ParsePredicate(createdContext.Exprs));
        }
        else if (context.Scope.When is { } whenContext)
        {
            EntityType type = Database.GetEntityType(whenContext.TypeId.Text);
            if (!type.Id.IsValid)
                AddError(StoryParser.ErrorCode.UnknownPropertyType, whenContext.TypeId.Span, whenContext.TypeId.Text);

            DeclareVar("$old", type.RefType, whenContext.Keyword.Span, out _);
            DeclareVar("$new", type.RefType, whenContext.Keyword.Span, out _);
            CurrentEventTrigger.When = (EventTrigger.WhenType.Changed, type.Id, ParsePredicate(whenContext.Exprs));
        }

        Database.Triggers.Add(CurrentEventTrigger);
        foreach (var effectContext in context.Scope.Effects)
        {
            var effect = ParseEffect(effectContext, out _);
            if (effect != null)
                CurrentEventTrigger.Effects.Add(effect);
        }

        CurrentEventTrigger.DebugScopeRoot = ConvertScope(rootScope);
        CurrentEventTrigger = null;
    }

    private IInstruction ParseEffect(EffectNode effectContext, out PropertyValue.ValueType type)
    {
        // Single funnel for every statement (events, triggers, if/else, match cases,
        // each/create bodies, function bodies all reach here). Attaching the source span
        // here gives the debugger line-granular breakpoints with one touch point.
        var instr = ParseEffectInner(effectContext, out type);
        if (instr != null)
        {
            var span = new FileRange(effectContext.Span).ToSpan();
            instr.Source = span;
            if (span.IsValid)
                Database.DebugStatementLines.Add(span.StartLine);
        }

        return instr;
    }

    private IInstruction ParseEffectInner(EffectNode effectContext, out PropertyValue.ValueType type)
    {
        if (effectContext.Expr != null)
        {
            var value = ParseExpr(effectContext.Expr, out type);
            if (value != null)
                return new CallInstruction(value);
        }

        type = PropertyValue.ValueType.Null;
        if (effectContext.Var != null)
            return ParseLocalVar(effectContext.Var);
        if (effectContext.Set != null)
            return ParseSet(effectContext.Set);
        if (effectContext.Init != null)
            return ParseInit(effectContext.Init);

        AddError(StoryParser.ErrorCode.Exception, effectContext.Span, "NULL");
        return new SetProperty(default, null, false);
    }

    private bool _parsingMatchCase;

    // > 0 while parsing a pick/each predicate, which is compiled to SQL. User-function calls inside
    // such a predicate are inlined into the query (UserFunctionCall.ToSql) rather than executed as
    // steppable instructions, so we flag those call sites for the editor.
    internal int InSqlPredicateDepth;
    internal bool InSqlPredicate => InSqlPredicateDepth > 0;

    private IValue ParseMatch(MatchNode match, out PropertyValue.ValueType valueType)
    {
        bool weight = match.IsWeight;
        var values = match.Exprs.Select(ParseExpr).ToArray();
        (int, IInstruction[])[] weights = default;
        (IValue?[], IInstruction[])[] cases = default;
        if (weight)
        {
            if (match.Exprs.Length > 1)
                AddError(StoryParser.ErrorCode.WeightMatchTakesOnlyOneValue, match.Exprs[1].Span,
                    match.Exprs.Length.ToString());
            weights = new (int, IInstruction[])[match.Cases.Length];
        }
        else
        {
            cases = new (IValue?[], IInstruction[])[match.Cases.Length];
        }

        int accWeight = 0;
        valueType = PropertyValue.ValueType.Null;
        for (int i = 0; i < match.Cases.Length; i++)
        {
            var caseCtx = match.Cases[i];
            _parsingMatchCase = true;
            IValue[] caseValues;
            try
            {
                // TODO type ?
                caseValues = caseCtx.Values.Select(x => ParseValue(x, out _)).ToArray();
            }
            finally
            {
                _parsingMatchCase = false;
            }

            using var caseScopeDisposable =
                new VariableDeclarationScopeDisposable(this, caseCtx.Scope?.Span ?? caseCtx.Effect?.Span);
            var instrs = caseCtx.Scope == null
                ? new[] {ParseEffect(caseCtx.Effect!, out valueType)}
                : ParseRawScope(caseCtx.Scope, out valueType);
            if (weight)
            {
                int w;
                if (caseValues[0] is MatchAnyValue)
                {
                    if (i != match.Cases.Length - 1)
                        AddError(StoryParser.ErrorCode.MatchAnyValueMustBeLast, caseCtx.Values[0].Span,
                            GetText(caseCtx.Values[0].Span));
                    weights[i] = (-1, instrs);
                }
                else
                {
                    w = ((Literal) caseValues[0]).Value.IntValue;
                    if (w <= 0)
                        AddError(StoryParser.ErrorCode.MatchNullWeight, caseCtx.Values[0].Span,
                            GetText(caseCtx.Values[0].Span));
                    accWeight += w;
                    weights[i] = (accWeight, instrs);
                }
            }
            else
                cases[i] = (caseValues, instrs);
        }

        if (weight)
            return new MatchWeight(values[0], weights);

        return new Match(values, cases);
    }

    private If ParseIf(IfNode @if, out PropertyValue.ValueType valueType)
    {
        var elseType = PropertyValue.ValueType.Null;
        var iff = new If(ParseExpr(@if.Cond), ParseScope(@if.Then, out var ifType),
            @if.Else == null ? Array.Empty<IInstruction>() : ParseScope(@if.Else, out elseType));
        valueType = @if.Else == null ? ifType : Cast(ifType, elseType);
        return iff;
    }

    public IValue ParsePredicate(ExprNode[] exprContexts)
    {
        var exprs = exprContexts;
        if (exprContexts.Length == 0) return null!;

        var predicate = exprs.Length == 1
            ? ParseExpr(exprs[0])!
            : new And(exprs.Select(x => ParseExpr(x)).Where(e => e != null).Cast<IValue>().ToList());
        return predicate;
    }

    private SetProperty ParseLocalVar(VarNode context)
    {
        var expr = ParseExpr(context.Expr, out var type);
        DeclareVar(context.VarId.Text, type, context.VarId.Span, out var varIndex);
        return new SetProperty(new PropertyPath(varIndex, type), expr, true);
    }

    private SetProperty ParseSet(SetNode context)
    {
        var left = ParsePath(context.Path, out var assignedType);
        var right = ParseExpr(context.Expr, out var rightType); //, left.Property);
        if (assignedType != Cast(assignedType, rightType))
            AddError(StoryParser.ErrorCode.MismatchedAssignmentTypes, context.Span, $"{assignedType} != {rightType}");
        return new SetProperty(left, right, false);
    }

    // `prop := value` — object-initializer assignment of `prop` on the current scope entity
    // (the last-declared variable, e.g. the entity created by an enclosing `create ... { }` block).
    private SetProperty ParseInit(InitNode context)
    {
        var propName = context.PropertyId.Text;
        int variableIndex = _current.Count - 1;
        EntityType owningType = Database.GetEntityType(_current[variableIndex].Type)!;
        var path = new PropertyPath(variableIndex, owningType.RefType);

        var propertyId = owningType.GetPropertyId(propName);
        PropertyValue.ValueType assignedType = default;
        if (!propertyId.IsValid)
            AddError(StoryParser.ErrorCode.UnknownProperty, context.PropertyId.Span, propName);
        else
        {
            assignedType = owningType.GetPropertyType(propName);
            Linker?.LinkProperty(new FileRange(context.PropertyId.Span), propertyId);
            path.AddProperty(propertyId);
        }

        var right = ParseExpr(context.Expr, out var rightType);
        if (assignedType != Cast(assignedType, rightType))
            AddError(StoryParser.ErrorCode.MismatchedAssignmentTypes, context.Span, $"{assignedType} != {rightType}");
        return new SetProperty(path, right, false, isInit: true);
    }

    static PropertyValue.ValueType Cast(PropertyValue.ValueType to, PropertyValue.ValueType from)
    {
        if (to == from)
            return to;

        if (to == PropertyValue.TypeFloat)
        {
            if (from == PropertyValue.TypeNumber || from == PropertyValue.TypePercent)
                return PropertyValue.TypeFloat;
        }

        if (to == PropertyValue.TypePercent)
        {
            if (from == PropertyValue.TypeNumber || from == PropertyValue.TypeFloat)
                return PropertyValue.TypePercent;
        }

        if (to.BaseType == PropertyValue.ValueBaseType.Enum && from == PropertyValue.TypeNumber)
            return to;
        if (from.BaseType == PropertyValue.ValueBaseType.Enum && to == PropertyValue.TypeNumber)
            return to;
        if (to.IsRefType && from.IsRefType && from.Index == 0) // null or (shaky) untyped ref
            return to;

        return from;
    }

    public IValue ParseValue(ValueNode value, out PropertyValue.ValueType type)
    {
        if (_parsingMatchCase && value.Path != null && value.Path.Span.ToStringValue() == "_")
        {
            // TODO ?
            type = default;
            return MatchAnyValue.Instance;
        }

        if (value.TypeId != null)
        {
            var etype = Database.GetEntityType(value.TypeId.Value.Text);
            if (!etype.Id.IsValid)
            {
                if (Database.GetEnumDefinition(value.TypeId.Value.Text, out var ed))
                {
                    // TODO really ?
                    type = ed.ValueType;
                    return new Literal(ed.EnumType);
                }

                AddError(StoryParser.ErrorCode.UnknownPropertyType, value.TypeId.Value.Span, value.TypeId.Value.Text);
            }

            type = etype.RefType;
            return new Literal(etype.Id);
        }

        if (value.Call != null)
            return ParseCall(value.Call, out type);

        if (value.RawCall != null)
            return ParseRawCall(value.RawCall, out type);

        if (value.Path != null)
        {
            var path = ParsePath(value.Path, out type);
            return path;
        }

        if (value.StringLit != null)
        {
            type = PropertyValue.TypeString;
            return ParseInterpolatedString(value.StringLit);
        }

        if (value.IsNull)
        {
            type = PropertyValue.TypeRef;
            return new Literal(EntityId.Null);
        }

        if (value.Number is { } number)
        {
            if (number.Kind == NumberKind.Float)
            {
                type = PropertyValue.TypeFloat;
                return new Literal(float.Parse(number.Text));
            }

            if (number.Kind == NumberKind.Percent)
            {
                type = PropertyValue.TypePercent;
                return new Literal(PropertyValue.Percent(int.Parse(number.Text.Substring(0, number.Text.Length - 1))));
            }

            type = PropertyValue.TypeNumber;
            return new Literal(int.Parse(number.Text));
        }

        if (value.BoolValue.HasValue)
        {
            type = PropertyValue.TypeBool;
            return new Literal(value.BoolValue.Value);
        }

        var enumValueContext = value.EnumValue;
        if (ParseEnum(out type, enumValueContext, out var addError)) return addError;

        throw new ArgumentOutOfRangeException();
    }

    private bool ParseEnum<T>(ValueNode valueContext, out T val) where T : struct, Enum
    {
        Ident enumValue;

        if (valueContext.EnumValue != null)
        {
            var enumType = valueContext.EnumValue.EnumType;
            if (enumType.Text != typeof(T).Name)
            {
                val = default;
                AddError(StoryParser.ErrorCode.MismatchedAssignmentTypes, enumType.Span,
                    "Expected an enum of type " + typeof(T).Name);
                return false;
            }

            enumValue = valueContext.EnumValue.Member;
        }
        else
            enumValue = valueContext.TypeId!.Value;

        return Enum.TryParse(enumValue.Text, out val);
    }

    private bool ParseEnum(out PropertyValue.ValueType type, EnumValueNode? enumValueContext, out IValue addError)
    {
        if (enumValueContext != null)
        {
            var enumType = enumValueContext.EnumType;
            if (!Database.GetEnumDefinition(enumType.Text, out var enumDef))
            {
                type = default;
                {
                    addError = (AddError(StoryParser.ErrorCode.UnknownEnum, enumType.Span, enumType.Text) as IValue)!;
                    return true;
                }
            }

            Linker?.LinkEnum(new FileRange(enumType.Span), enumDef.Index);

            var enumValue = enumValueContext.Member;
            if (!enumDef.GetValueFromName(enumValue.Text, out var val))
            {
                type = default;
                {
                    addError = (AddError(StoryParser.ErrorCode.UnknownEnumValue, enumValue.Span,
                        enumValue.Text + " in enum " + enumDef.Name) as IValue)!;
                    return true;
                }
            }

            Linker?.LinkEnumMember(new FileRange(enumValue.Span), val);
            type = enumDef.ValueType;
            {
                addError = new Literal(val);
                return true;
            }
        }

        type = default;
        addError = default!;
        return false;
    }

    public VariableDeclaration DeclareVar(string variable, PropertyValue.ValueType type, TextSpan contextStart, out int varIndex)
    {
        var variableDeclaration = new VariableDeclaration(variable, type, new FileRange(contextStart));
        _current.Variables.Add(variableDeclaration);
        Linker?.DeclareVariable(variableDeclaration.DeclarationRange, variableDeclaration, variableScope: _current.Range);
        varIndex = _current.Count - 1;
        return variableDeclaration;
    }

    public struct VariableDeclarationScopeDisposable : IDisposable
    {
        private readonly AstVisitor _astVisitor;
        private readonly TextSpan _scope;

        public VariableDeclarationScopeDisposable(AstVisitor astVisitor, TextSpan? scope)
        {
            if (scope == null)
                return;
            _astVisitor = astVisitor;
            _astVisitor.PushScope(scope.Value);
            _scope = scope.Value;
        }

        public void Dispose()
        {
            _astVisitor?.PopScope();
        }
    }

    private void PushScope(TextSpan scope)
    {
        Parser.VariableDeclarationScope newScope = new(_current, new FileRange(scope));
        _current.Children.Add(newScope);
        _current = newScope;
    }

    private void PopScope()
    {
        if (_current.Parent == null)
            throw new InvalidOperationException("Null parent scope");
        _current = _current.Parent!;
    }

    /// Capture the current lexical scope as an engine-side <see cref="Moirai.Core.DebugScope"/>.
    /// Used for triggers built by hand outside Visit* (e.g. a <c>schedule(...)</c> body) so the
    /// debugger can still resolve their locals (<c>$self</c>, body vars).
    public Moirai.Core.DebugScope CaptureCurrentDebugScope() => ConvertScope(_current);

    /// Translate the parser's lexical scope tree into the engine-side <see cref="Moirai.Core.DebugScope"/>
    /// the debugger uses to resolve value-stack slots back to variable names.
    private static Moirai.Core.DebugScope ConvertScope(Parser.VariableDeclarationScope scope)
    {
        var d = new Moirai.Core.DebugScope(scope.Range.ToSpan());
        for (int i = 0; i < scope.Variables.Count; i++)
            d.AddVariable(scope.ParentCount + i, scope.Variables[i].Name);
        foreach (var child in scope.Children)
            d.AddChild(ConvertScope(child));
        return d;
    }

    private IValue ParseRawCall(RawCallNode context, out PropertyValue.ValueType returnType)
    {
        var funcName = context.FunId.Text;
        if (Database.GetFunctionDefinition(funcName, out var fd))
        {
            var ctx = new FunctionParseContext(this, context, fd.Value);
            return ParseUserFunctionCall(this, ctx, out returnType);
        }

        if (StoryParser.GetFunctionDescriptor(funcName, out var f))
            return f.Parse(this, context, out returnType);

        returnType = default!;
        return (AddError(StoryParser.ErrorCode.UnknownInstruction, context.Span, funcName) as IValue)!;
    }


    private IValue ParseCall(CallNode context, out PropertyValue.ValueType returnType)
    {
        var funcName = context.FunId.Text;
        if (Database.GetFunctionDefinition(funcName, out var fd))
        {
            var ctx = new FunctionParseContext(this, context, fd.Value);
            Linker?.LinkFunction(new FileRange(context.FunId.Span), new UserFunctionDescriptor(fd.Value));
            if (InSqlPredicate)
                AddInfo(StoryParser.ErrorCode.FunctionInlinedToSql, context.Span,
                    $"'{funcName}' is inlined into the SQL query here — its body is not executed step-by-step, so breakpoints inside it won't hit for this call.");
            return ParseUserFunctionCall(this, ctx, out returnType);
        }

        if (StoryParser.GetFunctionDescriptor(funcName, out var f))
        {
            Linker?.LinkFunction(new FileRange(context.FunId.Span), f);
            return f.Parse(this, context, out returnType);
        }

        returnType = default!;
        return (AddError(StoryParser.ErrorCode.UnknownInstruction, context.Span, funcName) as IValue)!;
    }

    internal UserFunctionCall ParseUserFunctionCall(AstVisitor astVisitor,
        FunctionParseContext ctx, out PropertyValue.ValueType returnType)
    {
        var definition = ctx.Definition!.Value;
        UserFunctionCall call = new(definition,
            // TODO check arg/param type
            definition.Parameters
                // .Skip(definition.IsInstanceMethod ? 1 : 0)
                .Select((p,i) =>
            {

                var argument = ctx.ParseArgument(i, out var type);
                if(argument == null)
                    AddError(StoryParser.ErrorCode.MissingArgument, ctx.CallContext.Span,
                        $"Missing argument {i}: {p.ParamName}: {astVisitor.Database.Printer.Print(p.ParamType)}");
                else if (type != p.ParamType)
                    AddError(StoryParser.ErrorCode.MismatchedAssignmentTypes,
                        ctx.GetArgumentToken(i)?.Span ?? ctx.CallContext.Span,
                        $"Expected {astVisitor.Database.Printer.Print(p.ParamType)} got {astVisitor.Database.Printer.Print(type)}");
                return argument;
            }).ToArray()
        );

        returnType = definition.ReturnType;
        return call;
    }

    public IInstruction[] ParseScope(ScopeNode? scopeContext,
        out PropertyValue.ValueType type)
    {
        type = PropertyValue.ValueType.Null;
        if (scopeContext == null)
            return Array.Empty<IInstruction>();
        using var vs = new VariableDeclarationScopeDisposable(this, scopeContext.Span);

        return ParseRawScope(scopeContext, out type);
    }

    public IInstruction[] ParseRawScope(ScopeNode? scopeContext, out PropertyValue.ValueType type)
    {
        var ttype = PropertyValue.ValueType.Null;
        if (scopeContext == null)
        {
            type = ttype;
            return Array.Empty<IInstruction>();
        }

        var instructions = scopeContext.Effects.Select(x => { return ParseEffect(x, out ttype); })
            .Where(e => e != null).ToArray();
        type = ttype;
        return instructions;
    }

    public InterpolatedString ParseInterpolatedString(StringNode? stringContext)
    {
        if (stringContext == null || stringContext.Parts.Length == 0)
            return new InterpolatedString("", Array.Empty<IValue>());

        List<IValue> paths = new();
        string result = "";
        foreach (var part in stringContext.Parts)
        {
            if (part is StringTextPart textPart)
                result += textPart.Text.Replace("\\'", "'");
            else if (part is StringExprPart exprPart)
            {
                result += $"{{{paths.Count}}}";
                paths.Add(ParseExpr(exprPart.Expr)!);
            }
        }

        return new InterpolatedString(result, paths.ToArray());
    }

    public IValueSql? ParseExprSql(ExprNode context)
    {
        IValue v = ParseExpr(context)!;
        if (v is IValueSql sql)
            return sql;
        AddError(StoryParser.ErrorCode.ExpectedSql, context.Span, GetText(context.Span));
        return null;
    }

    public IValue? ParseExpr(ExprNode? context) => ParseExpr(context, out _);

    public IValue? ParseExpr(ExprNode? context, out PropertyValue.ValueType type)
    {
        if (context == null)
        {
            type = PropertyValue.ValueType.Null;
            return null;
        }

        if (context.If != null)
            return ParseIf(context.If, out type);
        if (context.Match != null)
            return ParseMatch(context.Match, out type);
        if (context.Value != null)
            return ParseValue(context.Value, out type);
        if (context.Paren != null)
            return ParseExpr(context.Paren, out type);

        string op = context.Op!;
        // left, alive
        IValue leftPath = ParseExpr(context.Left, out var leftType)!;

        // right, true or $x -  not alive or $x.alive
        IValue rightValue = ParseExpr(context.Right, out var rightType)!;

        BinaryOperator.Operator pop;
        switch (op)
        {
            case "and":
                pop = BinaryOperator.Operator.And;
                type = PropertyValue.TypeBool;
                break;
            case "or":
                pop = BinaryOperator.Operator.Or;
                type = PropertyValue.TypeBool;
                break;
            case "??":
                pop = BinaryOperator.Operator.Coalesce;
                type = rightType;
                break;
            case "=":
                type = PropertyValue.TypeBool;
                pop = BinaryOperator.Operator.Equals;

                if (leftPath is PropertyPath {Nested: false} p &&
                    (p.Segments == null || p.Segments[0].Property == Database.PropType) &&
                    rightValue is Literal l &&
                    l.Value.Type == PropertyValue.TypeEntityType)
                {
                    type = l.Value.Type;
                    return new IsOfType(leftPath, l.Value.TypeId);
                }

                break;
            case "!=":
                type = PropertyValue.TypeBool;
                pop = BinaryOperator.Operator.NotEquals;
                break;
            case "+":
                type = rightType;
                pop = BinaryOperator.Operator.Add;
                break;
            case "-":
                type = rightType;
                pop = BinaryOperator.Operator.Sub;
                break;
            case "/":
                type = rightType;
                pop = BinaryOperator.Operator.Div;
                break;
            case "*":
                type = rightType;
                pop = BinaryOperator.Operator.Mul;
                break;
            case "%":
                type = PropertyValue.TypeNumber;
                pop = BinaryOperator.Operator.Mod;
                break;
            case ">":
                type = PropertyValue.TypeBool;
                pop = BinaryOperator.Operator.Gt;
                break;
            case "<":
                type = PropertyValue.TypeBool;
                pop = BinaryOperator.Operator.Lt;
                break;
            case ">=":
                type = PropertyValue.TypeBool;
                pop = BinaryOperator.Operator.Ge;
                break;
            case "<=":
                type = PropertyValue.TypeBool;
                pop = BinaryOperator.Operator.Le;
                break;
            default:
                type = default;
                return (IValue?) AddError(StoryParser.ErrorCode.UnknownExpressionOperator, context.Span, op);
        }

        return new BinaryOperator(pop, leftPath, rightValue);
    }

    public string GetText(TextSpan span) => span.ToStringValue();

    public object AddError(StoryParser.ErrorCode code, TextSpan loc, string msg)
    {
        Errors.Add(new StoryParser.Error(code, loc, GetText(loc) + ": " + msg, Offset));
        // to avoid warnings in a case where the parsing is already compromised
        return null!;
    }

    public void AddWarning(StoryParser.ErrorCode code, TextSpan loc, string msg)
    {
        Errors.Add(new StoryParser.Error(code, loc, GetText(loc) + ": " + msg, Offset,
            StoryParser.Severity.Warning));
    }

    // Informational, non-error annotations (e.g. "inlined into SQL"). Kept OUT of Errors so the
    // web server's query/parse paths — which treat any Errors entry as a failure — are unaffected;
    // the language server surfaces these separately as Information diagnostics.
    public readonly List<StoryParser.Error> InfoMarkers = new();

    public void AddInfo(StoryParser.ErrorCode code, TextSpan loc, string msg)
    {
        InfoMarkers.Add(new StoryParser.Error(code, loc, msg, Offset, StoryParser.Severity.Information));
    }

    private void ParseProperty(ref PropertyPath path, PathNode context,
        EntityType owningType, out PropertyValue.ValueType type)
    {
        // TODO path without var isn't implemented ? prop1.prop2 ?

        StoryParser.PathParser pathParser = new(this, context);
        type = default;
        if (context.PropertyId is { } rootProp)
        {
            pathParser.ParseProperty(ref path, rootProp, owningType, out type);
        }

        if (context.DotProperties.Length > 0)
            pathParser.Rec(ref path, 0, owningType, out type);
    }


    public PropertyPath ParsePath(PathNode context, out PropertyValue.ValueType type)
    {
        if (context.SingletonId is { } singletonId)
        {
            string typeName = singletonId.Text.Substring(1);
            EntityType singletonType = Database.GetEntityType(typeName);
            if (!singletonType.Id.IsValid)
            {
                AddError(StoryParser.ErrorCode.UnknownEntityType, singletonId.Span, typeName);
                type = default;

                return default;
            }

            Linker?.LinkType(new FileRange(singletonId.Span), singletonType.Id);

            // TODO chained singleton #Time.x.y
            var path = new PropertyPath(singletonType.Id);
            ParseProperty(ref path, context, singletonType, out type);
            return path;
        }

        int variableIndex;
        if (context.VarId is { } varName)
        {
            VariableDeclaration decl;
            if (!int.TryParse(varName.Text.Substring(1), out variableIndex))
            {
                variableIndex = _current.GetVariableIndexByName(varName.Text, out decl);
                if (variableIndex == -1)
                {
                    AddError(StoryParser.ErrorCode.VariableNotDeclared, context.Span, varName.Text);
                    type = default;
                    return new PropertyPath();
                }
            }
            else
                decl = _current[variableIndex];

            type = _current[variableIndex].Type;
            Linker?.LinkVariable(new FileRange(varName.Span), decl);
            if (context.DotProperties.Length == 0)
            {
                return new PropertyPath(variableIndex, type);
            }
        }
        else
            variableIndex = _current.Count - 1;

        {
            EntityType? etype = Database.GetEntityType(_current[variableIndex].Type);
            var path = new PropertyPath(variableIndex, etype.RefType);
            ParseProperty(ref path, context, etype, out type);
            return path;
        }
    }
}
