using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace Moirai.Parser;

public record FunctionParseContext(AstVisitor Visitor, ParserRuleContext CallContext, FunctionDefinition? Definition, PropertyPath? SelfPath = null)
{
    public int ParseVariable(out EntityTypeId entityTypeId, out PropertyValue.ValueType type)
    {
        EntityType varType = ParseEntityType();

        int varIndex;
        if (CallContext is MoiraiParser.CallContext c)
        {
            Visitor.DeclareVar(c.VAR_ID().GetText(), varType.RefType, c.VAR_ID().Symbol, out varIndex);
        }
        else
        {
            var rawCallContext = (MoiraiParser.Raw_callContext) CallContext;
            Visitor.DeclareVar(rawCallContext.VAR_ID().GetText(), varType.RefType, rawCallContext.VAR_ID().Symbol,
                out varIndex);
        }

        entityTypeId = varType.Id;
        type = varType.RefType;
        return varIndex;
    }

    public ParserRuleContext GetArgumentToken(int index)
    {
        if (CallContext is MoiraiParser.CallContext c)
        {
            return c.expr(index);
        }
        else if (CallContext is MoiraiParser.Raw_callContext r)
        {
            return r.value();
        }

        return CallContext;
    }

    public IValue ParseArgument(int index, out PropertyValue.ValueType type)
    {
        bool hasInstanceParam = Definition.HasValue && Definition.Value.IsInstanceMethod && SelfPath.HasValue;
        if (index == 0 && hasInstanceParam)
        {
            type =  (SelfPath.GetValueOrDefault().Segments?.Count ?? 0) != 0
                ? PropertyValue.TypeTypedRef(SelfPath.Value.Segments[^1].TypeId)
                : SelfPath.Value.TypeId;
            return SelfPath.Value;
        }
        if (CallContext is MoiraiParser.CallContext c)
        {
            return Visitor.ParseExpr(c.expr(index - (hasInstanceParam ? 1 : 0)), out type)!;
        }
        else if (CallContext is MoiraiParser.Raw_callContext r)
        {
            if (index != 0)
            {
                Visitor.AddError(StoryParser.ErrorCode.MissingArgument, CallContext,
                    "Expected more arguments, convert to () syntax");
                type = default;
                return default!;
            }

            return Visitor.ParseValue(r.value(), out type);
        }

        type = default;
        return default!;
    }
    public IValue ParseArgument(int index)
    {
        return ParseArgument(index, out _);
    }

    public int ArgCount => CallContext is MoiraiParser.CallContext c
        ? c.expr().Length
        : (CallContext is MoiraiParser.Raw_callContext r && r.value() != null ? 1 : 0);

    public EntityType ParseEntityType()
    {
        ITerminalNode t;
        if (CallContext is MoiraiParser.CallContext c)
            t = StoryParser.GetTypeTerminal(c.type());
        else
            t = StoryParser.GetTypeTerminal(((MoiraiParser.Raw_callContext) CallContext).type());
        EntityTypeId type = Visitor.Database.GetEntityType(Visitor.ParseType(t))?.Id ?? EntityTypeId.Null;
            
        Visitor.Linker?.LinkType(new FileRange(t.Symbol), type);
        if (type == EntityTypeId.Null)
        {
            Visitor.AddError(StoryParser.ErrorCode.UnknownEntityType, GetArgumentToken(0), $"'{type}'");
        }

        return this.Visitor.Database.Types[(int) type.Id];
    }

    public string GetText(RuleContext expr) => Visitor.Parser.TokenStream.GetText(expr);

    public MoiraiParser.ScopeContext GetScopeContext()
    {
        MoiraiParser.ScopeContext scopeContext = CallContext is MoiraiParser.CallContext c
            ? c.scope()
            : ((MoiraiParser.Raw_callContext)CallContext).scope();
        return scopeContext;
    }

    public void ExpectArgcount(int i, bool isMaxCount = false)
    {
        if (isMaxCount ? ArgCount > i : ArgCount != i)
            Visitor.AddError(StoryParser.ErrorCode.MissingArgument, CallContext,
                $"Expected {i} arguments{(isMaxCount ? " max" : "")}, got {ArgCount}");
    }

    public IValueSql ParsePredicateSql(EntityTypeId entityTypeId)
    {
        IValue v = ParsePredicate(entityTypeId);
        if (v is IValueSql sql)
            return sql;
        Visitor.AddError(StoryParser.ErrorCode.ExpectedSql, CallContext, "Expected SQL expression");
        return null!;
    }
    public IValue ParsePredicate(EntityTypeId entityTypeId)
    {
        if (ArgCount == 1)
            return ParseArgument(0);
        IValue[] preds = new IValue[ArgCount];
        for (int i = 0; i < ArgCount; i++)
        {
            preds[i] = ParseArgument(i);
        }

        return new And(preds);
    }
}
