using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace Moirai.Parser;

public class AstVisitor : MoiraiParserBaseVisitor<object?>, StoryParser.IVisitor
{
    public record struct VariableDeclaration(string Name, PropertyValue.ValueType Type, FileRange DeclarationRange)
    {
    }

    public (int offsetLine, int offsetColumn) Offset { get; set; }

    public readonly Parser.VariableDeclarationScope RootScope;
    private Parser.VariableDeclarationScope _current;
        
    public List<StoryParser.Error> Errors { get; } = new();

    public MoiraiParser Parser { get; set; }
    public StoryParser.ILinker? Linker { get; set; }
    protected override object? DefaultResult => null;

    public readonly Database Database;

    public AstVisitor(Database database, MoiraiParser parser)
    {
        Database = database;
        Parser = parser;
        RootScope = new(null, FileRange.Empty);
        _current = RootScope;
    }

    MoiraiParser.AttributeContext[] _currentAttribute;

    public override object? VisitR(MoiraiParser.RContext context)
    {
        void VisitDefinitionsOfType<T>(Func<MoiraiParser.DefContext, T> selector) where T: ParserRuleContext 
        {
            foreach (MoiraiParser.DefContext defContext in context.def())
            {
                _currentAttribute = defContext.attribute();
                if(selector(defContext) is { } parserRuleContext)
                    parserRuleContext.Accept(this);
                _currentAttribute = null!;
            }
        }   
        IEnumerable<(T, MoiraiParser.AttributeContext[])> Each<T>(Func<MoiraiParser.DefContext, T> selector) where T: ParserRuleContext 
        {
            foreach (MoiraiParser.DefContext defContext in context.def())
            {
                if(selector(defContext) is { } t)
                    yield return (t,defContext.attribute());
            }
        }

        VisitDefinitionsOfType(x => x.enum_definition());
        
        List<(EntityType Id, MoiraiParser.Type_definitionContext attr)> typesContexts = new();
        List<(EntityType Id, MoiraiParser.AttributeContext attr)> deferredTypeAttributes = new();

        foreach (var (typeDefinitionContext, attributes) in Each(x => x.type_definition()))
        {
            if (typeDefinitionContext.TYPE_ID() == null)
                return AddError(StoryParser.ErrorCode.TypenameMustStartWithUpperCase,
                    typeDefinitionContext,
                    typeDefinitionContext.GetText());

            string? typeName = typeDefinitionContext.TYPE_ID().GetText();
            EntityType type = DeclareEntityType(typeName);
            type.IsSingleton = typeDefinitionContext.SINGLETON() != null;

            Linker?.DeclareType(new FileRange(typeDefinitionContext), type.Id);

            typesContexts.Add((type, typeDefinitionContext));
            foreach (var attr in attributes)
                deferredTypeAttributes.Add((type, attr));
        }

        foreach (var (type, typeDefinitionContext) in typesContexts)
        {
            foreach (MoiraiParser.Prop_definitionContext? propDefinitionContext in typeDefinitionContext.prop_definition())
            {
                var propName = propDefinitionContext.property_id().GetText();
                if (type.GetPropertyId(propName).Id != 0)
                    return AddError(StoryParser.ErrorCode.DuplicatePropertyDefinition, typeDefinitionContext, propName);

                var propTypeCtx = propDefinitionContext.type();
                bool isCollection = propTypeCtx.LBRACK() != null;
                PropertyValue.ValueType proptype =
                    ParseType(StoryParser.GetTypeTerminal(propTypeCtx));
                var propertyDefinition = new PropertyDefinition(propName, type.Id, (uint) type.Properties.Count,
                    proptype, isCollection);
                type.Properties.Add(propertyDefinition);
                Linker?.DeclareTypeProperty(new FileRange(propDefinitionContext), propertyDefinition.PropertyId);
            }

            foreach (var functionDefinitionContext in typeDefinitionContext.function_definition())
            {
                ParseFunctionDefinition(functionDefinitionContext, type);
            }
        }

        foreach (var (tid, attr) in deferredTypeAttributes)
        {
            var id = attr.ID();
            if (id.GetText() != "display")
            {
                AddError(StoryParser.ErrorCode.UnknownAttribute, id, id.GetText() ?? "??");
                continue;
            }

            if (attr.expr().Length < 3)
            {
                AddError(StoryParser.ErrorCode.MissingArgument, attr,
                    "display expects two arguments, a string and and expression");
                continue;
            }

            var refReferencedType = ParseType(attr.expr(0).value().type_id().TYPE_ID());
            if (!refReferencedType.IsRefType)
                AddError(StoryParser.ErrorCode.UnknownEntityType, attr, "expected an Entity type");

            using (new VariableDeclarationScopeDisposable(this, attr))
            {
                DeclareVar("$self", tid.RefType, id.Symbol, out var varIndex);
                DeclareVar("$other", refReferencedType, id.Symbol, out var otherVarIndex);
                var expr = ParseExprSql(attr.expr(2))!;
                InterpolatedString? itemDisplay = null;
                if (attr.expr(3)?.value()?.@string() != null)
                    itemDisplay = ParseInterpolatedString(attr.expr(3).value().@string());
                Display d = new Display(Database.GetEntityType(refReferencedType)!, varIndex, otherVarIndex,
                    attr.expr(1).GetText(), expr, itemDisplay);
                var t = Database.Types[(int) (tid.Id.Id)];

                t.Attributes.Add(d);
            }
        }

        foreach (var (fundef, attrs) in Each(x => x.function_definition()))
        {
            ParseFunctionDefinition(fundef);
        }

        
        foreach (var child in context.def())
        {
            _currentAttribute = child.attribute();
            if (child.@event() is { } e)
                e.Accept(this);
            else if (child.trigger() is {} t)
                t.Accept(this);
            _currentAttribute = null;
        }

        return null;
    }

    private void ParseFunctionDefinition(MoiraiParser.Function_definitionContext fundef, EntityType? instanceType = null)
    {
        using var _ = new VariableDeclarationScopeDisposable(this, fundef.scope());
        var rootScope = _current;

        var name = fundef.fun_id().GetText();
        PropertyValue.ValueType returnType = PropertyValue.ValueType.Null;
        if(fundef.type() != null)
        {
            returnType = ParseType(StoryParser.GetTypeTerminal(fundef.type()));
        }

        if (instanceType != null)
        {
            var parentTypeContext = fundef.Parent as MoiraiParser.Type_definitionContext;
            DeclareVar("$self", instanceType.RefType, parentTypeContext.TYPE_ID().Symbol, out var varIndex);
        }
        
            
        var parameters = fundef.param().Select(p =>
        {
            var paramName = p.VAR_ID().GetText();
            var paramType = ParseType(StoryParser.GetTypeTerminal(p.type()));
            DeclareVar(paramName, paramType, p.VAR_ID().Symbol, out var paramIndex);
            return new FunctionDefinition.Parameter(paramName, paramType, paramIndex);
        }).ToArray();
        var functionDefinitionId = new FunctionDefinitionId(
            (ushort)(instanceType == null ? Database.Functions.Count : instanceType.Functions.Count));
        var instructions = ParseScope(fundef.scope(), out var actualType);
        var functionDefinition = new FunctionDefinition(functionDefinitionId,
            name,
            instanceType?.Id ?? EntityTypeId.Null,
            returnType,
            parameters,
            instructions,
            ConvertScope(rootScope));
        if (instanceType != null)
            instanceType.Functions.Add(functionDefinition);
        this.Database.Functions.Add(functionDefinition);
        // A function with no declared return type is a procedure: its body is effects (create/set/
        // record/call) and any trailing value is ignored, so only value functions check the body type.
        if (returnType != PropertyValue.ValueType.Null && actualType != returnType)
            AddError(actualType == PropertyValue.ValueType.Null ? StoryParser.ErrorCode.MissingReturnValue : StoryParser.ErrorCode.MismatchedReturnType, fundef, $"{actualType} != {returnType}");
        
        Linker?.DeclareFunction(new FileRange(fundef.fun_id()), new UserFunctionDescriptor(functionDefinition));
    }


    public override object? VisitType_definition(MoiraiParser.Type_definitionContext context)
    {
        throw new NotImplementedException();
    }

    public EntityType DeclareEntityType(string typeName)
    {
        var id = (uint) Database.Types.Count;
        var entityType = new EntityType(typeName, id);
        Database.Types.Add(entityType);
        return entityType;
    }

    public override object? VisitProp_definition(MoiraiParser.Prop_definitionContext context)
    {
        throw new NotImplementedException();
    }

    public PropertyValue.ValueType ParseType(ITerminalNode id)
    {
        switch (id.GetText())
        {
            case "bool": return PropertyValue.TypeBool;
            // case "ref": return PropertyValue.TypeRef;
            case "number": return PropertyValue.TypeNumber;
            case "float": return PropertyValue.TypeFloat;
            case "string": return PropertyValue.TypeString;
            case "percentage": return PropertyValue.TypePercent;
            default:
                if (Database.GetEnumDefinition(id.GetText(), out EnumDefinition enumDefinition))
                {
                    Linker?.LinkEnum(new(id), enumDefinition.Index);
                    return PropertyValue.TypeEnum(enumDefinition.Index);
                }
                var entityType = Database.GetEntityType(id.GetText());
                Linker?.LinkType(new(id), entityType.Id);
                if (entityType.Id.IsValid)
                    return entityType.RefType;
                AddError(StoryParser.ErrorCode.UnknownPropertyType, id, id.GetText());
                return default;
        }
    }

    public override object? VisitEnum_definition(MoiraiParser.Enum_definitionContext context)
    {
        EnumDefinition en = new(new EnumDefinitionId((ushort) Database.Enums.Count), context.TYPE_ID(0).GetText(),
            context.TYPE_ID().Skip(1).Select(v => v.GetText()).ToList());
        Database.Enums.Add(en);
        Linker?.DeclareEnum(context, en.Index);
        return null;
    }

    public override object? VisitEvent(MoiraiParser.EventContext context)
    {
        string actionId = context.ID().GetText();
        using var _ = new VariableDeclarationScopeDisposable(this, context.scope());
        var rootScope = _current;

        ParseAttributes(out var tags, out var f);


        CurrentEventTrigger = new EventTrigger(Database.Actions.Count + 1, actionId, false, f, tags:tags);
        foreach (MoiraiParser.EffectContext effectContext in context.scope().effect())
        {
            // if (effectContext.comment() != null)
            //     continue;
            var effect = ParseEffect(effectContext, out var _);
            // if (effect == null)
            // {
            //     AddError(StoryParser.ErrorCode.NullEffect, effectContext, effectContext.GetText());
            //     continue;
            // }

            CurrentEventTrigger.Effects.Add(effect);
        }

        CurrentEventTrigger.DebugScopeRoot = ConvertScope(rootScope);
        Database.Actions.Add(CurrentEventTrigger);
        CurrentEventTrigger = null;
        return null;
    }

    private void ParseAttributes(out List<string>? tags, out IFilter? f)
    {
        tags = null;
        f = null;
        if (_currentAttribute != null)
        {
            for (int i = 0; i < _currentAttribute.Length; i++)
            {
                var p = _currentAttribute[i];
                var args = p.expr();
                switch (p.attr.Text)
                {
                    case "tag":
                        tags ??= new();
                        MoiraiParser.ExprContext[] exprContexts = p.expr();
                        for (int j = 0; j < exprContexts.Length; j++)
                        {
                            tags.Add(exprContexts[j].value().@string().GetText());
                            
                        }
                        break;
                    case "start":
                        f = new FilterAtStart();
                        break;
                    case "frequency":
                        if (args.Length != 3)
                        {
                            AddError(StoryParser.ErrorCode.MissingArgument, p,
                                "frequency expects 3 arguments");
                        }

                        if (!ParseEnum<Database.Frequency>(args[1].value(), out var val))
                            // .TryParse<Database.Frequency>(args[1].GetText(), out var freq))
                            AddError(StoryParser.ErrorCode.UnknownEnum, args[1],
                                "Should be a value among " + string.Join(", ", Enum.GetNames<Database.Frequency>()));
                        var x = int.Parse(args[0].GetText());
                        var y = int.Parse(args[2].GetText());
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
                        AddError(StoryParser.ErrorCode.UnknownCall, p, "Unknown attribute");
                        break;
                    // case "every":
                    // {
                    //     var x = int.Parse(p.occurence.Text);
                    //     var y = int.Parse(p.years.Text);
                    //     f = new FilterExactlyXEveryYYears(x, y, Database.Actions.Count + 1);
                    //     break;
                    // }
                    // case "per":
                    // {
                    //     var x = int.Parse(p.occurence.Text);
                    //     var y = int.Parse(p.years.Text);
                    //     f = new FilterProbabilityXPerYears(x, y);
                    //     break;
                    // }
                }
            }
        }
    }

    public EventTrigger? CurrentEventTrigger;

    public override object? VisitTrigger(MoiraiParser.TriggerContext context)
    {
        string actionId = context.ID().GetText();
        ParseAttributes(out var tags, out var _);        
        CurrentEventTrigger = new EventTrigger(Database.Triggers.Count + 1, actionId, true, null, tags:tags);

        using var _ = new VariableDeclarationScopeDisposable(this, context.scope());
        var rootScope = _current;
        if (context.scope().when_created() is { } createdContext)
        {
            EntityType type = Database.GetEntityType(createdContext.type_id().TYPE_ID().GetText());
            if (!type.Id.IsValid)
                AddError(StoryParser.ErrorCode.UnknownPropertyType, createdContext,
                    createdContext.type_id().TYPE_ID()?.GetText() ?? createdContext.GetText());

            DeclareVar("$new", type.RefType, createdContext.WHEN_CREATED().Symbol, out var _);
            CurrentEventTrigger.When = (EventTrigger.WhenType.Created, type.Id,
                ParsePredicate(createdContext.expr()));
        }
        else if (context.scope().when() is { } whenContext)
        {
            EntityType type = Database.GetEntityType(whenContext.type_id().TYPE_ID().GetText());
            if (!type.Id.IsValid)
                AddError(StoryParser.ErrorCode.UnknownPropertyType, whenContext, whenContext.type_id().TYPE_ID().GetText());

            DeclareVar("$old", type.RefType, whenContext.WHEN().Symbol, out var _);
            DeclareVar("$new", type.RefType, whenContext.WHEN().Symbol, out var _);
            CurrentEventTrigger.When = (EventTrigger.WhenType.Changed, type.Id, ParsePredicate(whenContext.expr()));
        }

        Database.Triggers.Add(CurrentEventTrigger);
        foreach (var effectContext in context.scope().effect())
        {
            // if (effectContext.comment() != null)
            // continue;
            var effect = ParseEffect(effectContext, out var _);
            if (effect != null)
                CurrentEventTrigger.Effects.Add(effect);
        }

        CurrentEventTrigger.DebugScopeRoot = ConvertScope(rootScope);
        CurrentEventTrigger = null;
        return null;
    }

    private IInstruction ParseEffect(MoiraiParser.EffectContext effectContext, out PropertyValue.ValueType type)
    {
        // Single funnel for every statement (events, triggers, if/else, match cases,
        // each/create bodies, function bodies all reach here). Attaching the source span
        // here gives the debugger line-granular breakpoints with one touch point.
        var instr = ParseEffectInner(effectContext, out type);
        if (instr != null)
            instr.Source = new FileRange(effectContext).ToSpan();
        return instr;
    }

    private IInstruction ParseEffectInner(MoiraiParser.EffectContext effectContext, out PropertyValue.ValueType type)
    {
        if (effectContext.expr() != null)
        {
            var value = ParseExpr(effectContext.expr(), out type);
            if (value != null)
                return new CallInstruction(value);
        }

        type = PropertyValue.ValueType.Null;
        if (effectContext.var() != null)
            return ParseLocalVar(effectContext.var());
        if (effectContext.set() != null)
            return ParseSet(effectContext.set());
        if (effectContext.init() != null)
            return ParseInit(effectContext.init());

        AddError(StoryParser.ErrorCode.Exception, effectContext, "NULL");
        return new SetProperty(default, null, false);
    }

    private bool _parsingMatchCase;

    private IValue ParseMatch(MoiraiParser.MatchContext match, out PropertyValue.ValueType valueType)
    {
        bool weight = match.MATCH_WEIGHT() != null;
        var values = match.expr().Select(ParseExpr).ToArray();
        (int, IInstruction[])[] weights = default;
        (IValue?[], IInstruction[])[] cases = default;
        if (weight)
        {
            if (values.Length > 1)
                AddError(StoryParser.ErrorCode.WeightMatchTakesOnlyOneValue, match.expr(1), values.Length.ToString());
            weights = new (int, IInstruction[])[match.match_case().Length];
        }
        else
        {
            cases = new (IValue?[], IInstruction[])[match.match_case().Length];
        }

        int accWeight = 0;
        valueType = PropertyValue.ValueType.Null;
        for (int i = 0; i < match.match_case().Length; i++)
        {
            var caseCtx = match.match_case(i);
            _parsingMatchCase = true;
            IValue[] caseValues;
            try
            {
                // TODO type ?
                caseValues = caseCtx.value().Select(x => ParseValue(x, out var _)).ToArray();
            }
            finally
            {
                _parsingMatchCase = false;
            }

            using var _ = new VariableDeclarationScopeDisposable(this, (ParserRuleContext)caseCtx.scope() ?? caseCtx.effect());
            var instrs = caseCtx.scope() == null
                ? new[] {ParseEffect(caseCtx.effect(), out valueType)}
                : ParseRawScope(caseCtx.scope(), out valueType);
            if (weight)
            {
                int w;
                if (caseValues[0] is MatchAnyValue)
                {
                    if (i != match.match_case().Length - 1)
                        AddError(StoryParser.ErrorCode.MatchAnyValueMustBeLast, caseCtx.value(0), caseCtx.value(0).GetText());
                    weights[i] = (-1, instrs);
                }
                else
                {
                    w = ((Literal) caseValues[0]).Value.IntValue;
                    if (w <= 0)
                        AddError(StoryParser.ErrorCode.MatchNullWeight, caseCtx.value(0), caseCtx.value(0).GetText());
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

    private If ParseIf(MoiraiParser.IfContext @if, out PropertyValue.ValueType valueType)
    {
        var elseType = PropertyValue.ValueType.Null;
        var iff = new If(ParseExpr(@if.cond), ParseScope(@if.then, out var ifType),
            @if.@else == null ? Array.Empty<IInstruction>() : ParseScope(@if.@else, out elseType));
        valueType = @if.@else == null ? ifType : Cast(ifType, elseType);
        return iff;
    }

    public IValue ParsePredicate(MoiraiParser.ExprContext[] exprContexts)
    {
        var exprs = exprContexts;
        if (exprContexts.Length == 0) return null;

        var predicate = exprs.Length == 1
            ? ParseExpr(exprs[0])!
            : new And(exprs.Select(x => ParseExpr(x)).Where(e => e != null).Cast<IValue>().ToList());
        return predicate;
    }

    public override object? VisitWhen(MoiraiParser.WhenContext context)
    {
        throw new NotImplementedException();
    }

    public override object? VisitSet(MoiraiParser.SetContext context)
    {
        throw new NotImplementedException();

        return null;
    }

    private SetProperty ParseLocalVar(MoiraiParser.VarContext context)
    {
        var name = context.VAR_ID();
        var expr = ParseExpr(context.expr(), out var type);
        DeclareVar(name.GetText(), type, name.Symbol, out var varIndex);
        return new SetProperty(new PropertyPath(varIndex, type), expr, true);
    }

    private SetProperty ParseSet(MoiraiParser.SetContext context)
    {
        var left = ParsePath(context.path(), out var assignedType);
        var right = ParseExpr(context.expr(), out var rightType); //, left.Property);
        if (assignedType != Cast(assignedType, rightType))
            AddError(StoryParser.ErrorCode.MismatchedAssignmentTypes, context, $"{assignedType} != {rightType}");
        return new SetProperty(left, right, false);
    }

    // `prop := value` — object-initializer assignment of `prop` on the current scope entity
    // (the last-declared variable, e.g. the entity created by an enclosing `create ... { }` block).
    private SetProperty ParseInit(MoiraiParser.InitContext context)
    {
        var propName = context.property_id().GetText();
        int variableIndex = _current.Count - 1;
        EntityType owningType = Database.GetEntityType(_current[variableIndex].Type)!;
        var path = new PropertyPath(variableIndex, owningType.RefType);

        var propertyId = owningType.GetPropertyId(propName);
        PropertyValue.ValueType assignedType = default;
        if (!propertyId.IsValid)
            AddError(StoryParser.ErrorCode.UnknownProperty, context.property_id(), propName);
        else
        {
            assignedType = owningType.GetPropertyType(propName);
            Linker?.LinkProperty(new FileRange(context.property_id()), propertyId);
            path.AddProperty(propertyId);
        }

        var right = ParseExpr(context.expr(), out var rightType);
        if (assignedType != Cast(assignedType, rightType))
            AddError(StoryParser.ErrorCode.MismatchedAssignmentTypes, context, $"{assignedType} != {rightType}");
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

    public IValue ParseValue(MoiraiParser.ValueContext value, out PropertyValue.ValueType type)
    {
        if (_parsingMatchCase && value.path()?.GetText() == "_")
        {
            // TODO ?
            type = default;
            return MatchAnyValue.Instance;
        }

        if (value.type_id()?.TYPE_ID() != null)
        {
            var etype = Database.GetEntityType(value.type_id().TYPE_ID().GetText());
            if (!etype.Id.IsValid)
            {
                if (Database.GetEnumDefinition(value.type_id().TYPE_ID().GetText(), out var ed))
                {
                    // TODO really ?
                    type = ed.ValueType;
                    return new Literal(ed.EnumType);
                }

                AddError(StoryParser.ErrorCode.UnknownPropertyType, value, value.type_id().TYPE_ID().GetText());
            }

            type = etype.RefType;
            return new Literal(etype.Id);
        }

        if (value.call() != null)
        {
            return ParseCall(value.call(), out type);
        }

        if (value.raw_call() != null)
        {
            return ParseRawCall(value.raw_call(), out type);
        }

        if (value.path() != null)
        {
            var path = ParsePath(value.path(), out type);
            return path;
        }

        if (value.@string() != null)
        {
            type = PropertyValue.TypeString;
            return ParseInterpolatedString(value.@string());
        }

        if (value.NULL() != null)
        {
            type = PropertyValue.TypeRef;
            return new Literal(EntityId.Null);
        }

        if (value.number() is { } number)
        {
            if (number.NUMBER_FLOAT() != null)
            {
                type = PropertyValue.TypeFloat;
                return new Literal(float.Parse(number.NUMBER_FLOAT().GetText()));
            }

            if (number.PERCENT() != null)
            {
                type = PropertyValue.TypePercent;
                return new Literal(PropertyValue.Percent(int.Parse(number.PERCENT().GetText()
                    .Substring(0, number.PERCENT().GetText().Length - 1))));
            }

            type = PropertyValue.TypeNumber;
            return new Literal(int.Parse(number.GetText()));
        }

        if (value.@bool() != null)
        {
            type = PropertyValue.TypeBool;
            return new Literal(value.@bool().TRUE() != null);
        }

        var enumValueContext = value.enum_value();
        if (ParseEnum(out type, enumValueContext, out var addError)) return addError;

        throw new ArgumentOutOfRangeException();
    }

    private bool ParseEnum<T>(MoiraiParser.ValueContext valueContext, out T val) where T : struct, Enum
    {
        ITerminalNode enumValue;

        if (valueContext.enum_value() != null)
        {
            var enumType = valueContext.enum_value().TYPE_ID(0);
            if (enumType.GetText() != typeof(T).Name)
            {
                val = default;
                AddError(StoryParser.ErrorCode.MismatchedAssignmentTypes, enumType,
                    "Expected an enum of type " + typeof(T).Name);
                return false;
            }
            enumValue = valueContext.enum_value().TYPE_ID(1);
        }
        else
            enumValue = valueContext.type_id().TYPE_ID();

        return Enum.TryParse(enumValue.GetText(), out val);
    }

    private bool ParseEnum(out PropertyValue.ValueType type, MoiraiParser.Enum_valueContext? enumValueContext,
        out IValue addError)
    {
        if (enumValueContext != null)
        {
            var enumType = enumValueContext.TYPE_ID(0);
            if (!Database.GetEnumDefinition(enumType.GetText(), out var enumDef))
            {
                type = default;
                {
                    addError = (AddError(StoryParser.ErrorCode.UnknownEnum, enumType, enumType.GetText()) as IValue)!;
                    return true;
                }
            }
            Linker?.LinkEnum(new(enumType), enumDef.Index);

            var enumValue = enumValueContext.TYPE_ID(1);
            if (!enumDef.GetValueFromName(enumValue.GetText(), out var val))
            {
                type = default;
                {
                    addError = (AddError(StoryParser.ErrorCode.UnknownEnumValue, enumValue,
                        enumValue.GetText() + " in enum " + enumDef.Name) as IValue)!;
                    return true;
                }
            }

            Linker?.LinkEnumMember(new FileRange(enumValue), val);
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

    public VariableDeclaration DeclareVar(string variable, PropertyValue.ValueType type, IToken contextStart, out int varIndex)
    {
        // if ((varIndex = GetVariableIndexByName(variable)) != -1)
        // {
        //     // AddError(ErrorCode.DuplicateVariableDefinition,  contextStart, " Duplicate variable " + variable);
        //     // varIndex = 0;
        //     return true;
        // }

        var variableDeclaration = new VariableDeclaration(variable, type, new FileRange(contextStart));
        _current.Variables.Add(variableDeclaration);
        // Linker?.DeclareVariable(_current.Range, variableDeclaration);
        Linker?.DeclareVariable(variableDeclaration.DeclarationRange, variableDeclaration, variableScope: _current.Range);
        varIndex = _current.Count - 1;
        return variableDeclaration;
    }

    public struct VariableDeclarationScopeDisposable : IDisposable
    {
        private readonly AstVisitor _astVisitor;
        private readonly ParserRuleContext _scope;

        public VariableDeclarationScopeDisposable(AstVisitor astVisitor, ParserRuleContext scope)
        {
            if (scope == null)
                return;
            _astVisitor = astVisitor;
            _astVisitor.PushScope(scope);
            _scope = scope;
        }

        public void Dispose()
        {
            _astVisitor?.PopScope();
        }
    }

    private void PushScope(ParserRuleContext scope)
    {
        Parser.VariableDeclarationScope newScope = new(_current, new FileRange(scope));
        _current.Children.Add(newScope);
        _current = newScope;
    }
    private void PopScope()
    {
        if(_current.Parent == null)
            throw new InvalidOperationException("Null parent scope");
        _current = _current.Parent!;
    }

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

    private IValue ParseRawCall(MoiraiParser.Raw_callContext context, out PropertyValue.ValueType returnType)
    {
        var funcName = context.fun_id().GetText();
        if(Database.GetFunctionDefinition(funcName, out var fd))
        {
            var ctx = new FunctionParseContext(this, context, fd.Value);
            return ParseUserFunctionCall(this, ctx, out returnType);
        }
        if(StoryParser.GetFunctionDescriptor(funcName, out var f));
        {
            return f.Parse(this, context, out returnType);
        }

        returnType = default!;
        return (AddError(StoryParser.ErrorCode.UnknownInstruction, context, funcName) as IValue)!;
    }


    private IValue ParseCall(MoiraiParser.CallContext context, out PropertyValue.ValueType returnType)
    {
        var funcName = context.fun_id().GetText();
        if(Database.GetFunctionDefinition(funcName, out var fd))
        {
            var ctx = new FunctionParseContext(this, context, fd.Value);
            Linker?.LinkFunction(new FileRange(context.fun_id()), new UserFunctionDescriptor(fd.Value));
            return ParseUserFunctionCall(this,  ctx, out returnType);
        }
        if(StoryParser.GetFunctionDescriptor(funcName, out var f))
        {
            Linker?.LinkFunction(new FileRange(context.fun_id()), f);
            return f.Parse(this, context, out returnType);
        }

        returnType = default!;
        return (AddError(StoryParser.ErrorCode.UnknownInstruction, context, funcName) as IValue)!;
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
                   
                var argument =   ctx.ParseArgument(i, out var type);
                if(argument == null)
                    AddError(StoryParser.ErrorCode.MissingArgument, ctx.CallContext, $"Missing argument {i}: {p.ParamName}: {astVisitor.Database.Printer.Print(p.ParamType)}");
                else if (type != p.ParamType)
                    AddError(StoryParser.ErrorCode.MismatchedAssignmentTypes, ctx.GetArgumentToken(i), $"Expected {astVisitor.Database.Printer.Print(p.ParamType)} got {astVisitor.Database.Printer.Print(type)}");
                return argument;
            }).ToArray()
        );

        returnType = definition.ReturnType;
        return call;
    }

    public IInstruction[] ParseScope(MoiraiParser.ScopeContext? scopeContext, 
        out PropertyValue.ValueType type)
    {
        // TODO 
        type = PropertyValue.ValueType.Null;
        if (scopeContext == null)
            return Array.Empty<IInstruction>();
        using var vs = new VariableDeclarationScopeDisposable(this, scopeContext);

        return ParseRawScope(scopeContext, out type);
    }

    public IInstruction[] ParseRawScope(MoiraiParser.ScopeContext scopeContext, out PropertyValue.ValueType type)
    {
        var ttype = PropertyValue.ValueType.Null;
        if (scopeContext == null)
        {
            type = ttype;
            return new IInstruction[0];
        }

        var instructions = scopeContext.effect().Select(x => { return ParseEffect(x, out ttype); })
            .Where(e => e != null).ToArray();
        type = ttype;
        return instructions;
    }

    public InterpolatedString ParseInterpolatedString(MoiraiParser.StringContext? stringContext)
    {
        if (stringContext == null || stringContext.stringContent().Length == 0)
            return new InterpolatedString("", Array.Empty<IValue>());

        List<IValue> paths = new();
        string result = "";
        foreach (var part in stringContext.stringContent())
        {
            if (part.TEXT() != null)
                result += part.TEXT().GetText().Replace("\\'", "'");
            else
            {
                result += $"{{{paths.Count}}}";
                paths.Add(ParseExpr(part.expr())!);
            }
        }

        // var str = stringContext.GetText().TrimQuotes();
        // List<IValue> paths = new();
        // string result = "";
        // int i = -1;
        // var prev = i + 1;
        //
        // while (i < str.Length)
        // {
        //     i = str.IndexOf('{', i + 1);
        //     if (i == -1)
        //         break;
        //
        //     int j = str.IndexOf('}', i + 1);
        //     if (j == -1)
        //         throw new System.NotImplementedException(
        //             $"Missing curly brace in string: {str}, opening brace at {i}");
        //
        //     var pathStr = str.Substring(i + 1, j - i - 1);
        //     var path = StoryParser.ParseExpr(this, pathStr,
        //         stringContext.Start.Line - 1 /* +1 somewhere in the pipeline */,
        //         stringContext.Start.Column + i + 1 + /*quote*/ 1, out _);
        //     paths.Add(path!);
        //     // Console.WriteLine($"'{pathStr}'");
        //     if (i > prev)
        //         result += str.Substring(prev, i - prev);
        //     result += $"{{{paths.Count - 1}}}";
        //     i = j;
        //     prev = i + 1;
        // }
        //
        // if (prev < str.Length)
        //     result += (str.Substring(prev));
        // // Console.WriteLine($"res:'{result}'");
        var interpolatedString = new InterpolatedString(result, paths.ToArray());
        return interpolatedString;
    }

    public IValueSql? ParseExprSql(MoiraiParser.ExprContext context)
    {
        IValue v = ParseExpr(context);
        if (v is IValueSql sql)
            return sql;
        AddError(StoryParser.ErrorCode.ExpectedSql, context, context.GetText());
        return null;
    }
    public IValue? ParseExpr(MoiraiParser.ExprContext context)
    {
        return ParseExpr(context, out var _);
    }

    public IValue? ParseExpr(MoiraiParser.ExprContext context, out PropertyValue.ValueType type)
    {
        if (context == null)
        {
            type = PropertyValue.ValueType.Null;
            return null;
        }
        if (context.@if() != null)
            return ParseIf(context.@if(), out type);
        if (context.match() != null)
            return ParseMatch(context.match(), out type);
        if (context.value() != null)
        {
            return ParseValue(context.value(), out type);
            // ComputedValue v = ParseValue(context.value(0), PropertyValue.TypeBool);
        }

        if (context.paren_expr != null)
            return ParseExpr(context.paren_expr, out type);

        string op = context.op.Text;
        // left, alive
        IValue leftPath = ParseExpr(context.left, out var leftType)!;

        // right, true or $x -  not alive or $x.alive
        IValue rightValue = ParseExpr(context.right, out var rightType)!;

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
                return (IValue?) AddError(StoryParser.ErrorCode.UnknownExpressionOperator, context, op);
        }

        return new BinaryOperator(pop, leftPath, rightValue);
    }

    public object AddError(StoryParser.ErrorCode code, ParserRuleContext loc, string msg)
    {
        Errors.Add(new StoryParser.Error(code, loc, Parser.TokenStream.GetText(loc) + ": " + msg, Offset));
        // to avoid warnings in a case where the parsing is already compromised
        return null!;
    }

    public object? AddError(StoryParser.ErrorCode code, ITerminalNode loc, string msg)
    {
        Errors.Add(new StoryParser.Error(code, loc, msg, Offset));
        return null;
    }

    public void AddWarning(StoryParser.ErrorCode code, ParserRuleContext loc, string msg)
    {
        Errors.Add(new StoryParser.Error(code, loc, Parser.TokenStream.GetText(loc) + ": " + msg, Offset,
            StoryParser.Severity.Warning));
    }

    public override object? VisitCall(MoiraiParser.CallContext context)
    {
        throw new NotImplementedException();
    }

    public override object? VisitExpr(MoiraiParser.ExprContext context)
    {
        throw new NotImplementedException();
    }

    public override object? VisitPath(MoiraiParser.PathContext context)
    {
        throw new NotImplementedException();
    }

    private void ParseProperty(ref PropertyPath path, MoiraiParser.PathContext context,
        EntityType owningType, out PropertyValue.ValueType type)
    {
        // TODO path without var isn't implemented ? prop1.prop2 ?

        StoryParser.PathParser pathParser = new(this, context);
        type = default;
        if (context.property_id() is { } rootProp)
        {
            pathParser.ParseProperty(ref path, rootProp, owningType, out type);
                
        }

        if(context.dot_property(0) != null)
            pathParser.Rec(ref path, 0, owningType, out type);
    }


    public PropertyPath ParsePath(MoiraiParser.PathContext context, out PropertyValue.ValueType type)
    {
        // if (context.ID().Length > 1)
        // throw new Exception("expected two parts, got " + (context.ID().Length + 1));

        ITerminalNode? singletonId = context.var_id_read()?.SINGLETON_ID();
        if (singletonId != null)
        {
            string typeName = singletonId.GetText().Substring(1);
            EntityType singletonType = Database.GetEntityType(typeName);
            if (!singletonType.Id.IsValid)
            {
                AddError(StoryParser.ErrorCode.UnknownEntityType, singletonId, typeName);
                type = default;

                return default;
            }
            Linker?.LinkType(new(singletonId), singletonType.Id);

            // TODO chained singleton #Time.x.y
            var path = new PropertyPath(singletonType.Id);
            ParseProperty(ref path, context, singletonType, out type);
            return path;
        }

        int variableIndex;
        ITerminalNode? varName = context.var_id_read()?.VAR_ID();
        if (varName != null)
        {
            VariableDeclaration decl;
            if (!int.TryParse(varName.GetText().Substring(1), out variableIndex))
            {
                variableIndex = _current.GetVariableIndexByName(varName.GetText(), out decl);
                if (variableIndex == -1)
                {
                    AddError(StoryParser.ErrorCode.VariableNotDeclared, context, varName.GetText());
                    type = default;
                    return new PropertyPath();
                }
            }
            else
                decl = _current[variableIndex];

            type = _current[variableIndex].Type;
            Linker?.LinkVariable(new FileRange(varName), decl);
            if (context.dot_property().Length == 0)
            {
                return new PropertyPath(variableIndex, type);
            }
        }
        else
            variableIndex = _current.Count - 1;

        {
            EntityType? etype = Database.GetEntityType(_current[variableIndex].Type);
            var path = new PropertyPath(variableIndex, etype.RefType);
            ParseProperty(ref path, context,  etype, out type);
            return path;
        }
    }
}
