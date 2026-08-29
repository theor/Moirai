using Moirai.Parser.Ast;
using Superpower.Model;

namespace Moirai.Parser;

public record FunctionParseContext(AstVisitor Visitor, CallOrRawCall CallContext, FunctionDefinition? Definition, PropertyPath? SelfPath = null)
{
    public int ParseVariable(out EntityTypeId entityTypeId, out PropertyValue.ValueType type)
    {
        EntityType varType = ParseEntityType();

        var varId = (CallContext.Call?.VarId ?? CallContext.RawCall?.VarId)!.Value;
        Visitor.DeclareVar(varId.Text, varType.RefType, varId.Span, out var varIndex);

        entityTypeId = varType.Id;
        type = varType.RefType;
        return varIndex;
    }

    /// Uniform view of "the argument at `index`" regardless of call shape: for `call`, its `index`th
    /// parenthesized `expr`; for `raw_call`, its single bare `value` (wrapped as an `ExprNode` so
    /// callers don't need to care which shape they're looking at — mirrors how `expr: value` already
    /// wraps a bare value in the grammar). Null when out of range, matching the old ANTLR-generated
    /// indexed accessors' behavior.
    public ExprNode? GetArgumentToken(int index)
    {
        if (CallContext.Call is { } c)
            return index >= 0 && index < c.Args.Length ? c.Args[index] : null;

        var r = CallContext.RawCall!;
        if (index != 0 || r.Value == null)
            return null;
        return new ExprNode(null, null, r.Value, null, null, null, null, r.Value.Span);
    }

    public IValue ParseArgument(int index, out PropertyValue.ValueType type)
    {
        bool hasInstanceParam = Definition.HasValue && Definition.Value.IsInstanceMethod && SelfPath.HasValue;
        if (index == 0 && hasInstanceParam)
        {
            type = (SelfPath.GetValueOrDefault().Segments?.Count ?? 0) != 0
                ? PropertyValue.TypeTypedRef(SelfPath.Value.Segments[^1].TypeId)
                : SelfPath.Value.TypeId;
            return SelfPath.Value;
        }

        if (CallContext.Call != null)
        {
            var arg = GetArgumentToken(index - (hasInstanceParam ? 1 : 0));
            return Visitor.ParseExpr(arg, out type)!;
        }

        if (index != 0)
        {
            Visitor.AddError(StoryParser.ErrorCode.MissingArgument, CallContext.Span,
                "Expected more arguments, convert to () syntax");
            type = default;
            return default!;
        }

        return Visitor.ParseValue(CallContext.RawCall!.Value!, out type);
    }

    public IValue ParseArgument(int index) => ParseArgument(index, out _);

    /// <summary>
    /// Parses argument <paramref name="index"/> as a path whose final segment is a collection property
    /// (e.g. <c>$e.parents</c>), returning the full path (for printing) plus its split into the owner
    /// entity path and the collection <see cref="PropertyId"/>. Emits an error and returns false otherwise.
    /// </summary>
    public bool ParseCollectionPath(int index, out PropertyPath full, out PropertyPath owner, out PropertyId collProp)
    {
        full = default;
        owner = default;
        collProp = default;
        var arg = ParseArgument(index);
        if (arg is PropertyPath pp && pp.TrySplitCollection(out owner, out collProp)
                                   && Visitor.Database.IsCollectionProperty(collProp))
        {
            full = pp;
            return true;
        }

        Visitor.AddError(StoryParser.ErrorCode.ExpectedCollection, GetArgumentToken(index)?.Span ?? CallContext.Span,
            "expected a collection property path (e.g. $e.items)");
        return false;
    }

    public int ArgCount => CallContext.Call is { } c
        ? c.Args.Length
        : (CallContext.RawCall != null && CallContext.RawCall.Value != null ? 1 : 0);

    public EntityType ParseEntityType()
    {
        var typeName = (CallContext.Call?.DeclType ?? CallContext.RawCall?.DeclType)!.Name;
        EntityTypeId type = Visitor.Database.GetEntityType(Visitor.ParseType(typeName))?.Id ?? EntityTypeId.Null;

        Visitor.Linker?.LinkType(new FileRange(typeName.Span), type);
        if (type == EntityTypeId.Null)
            Visitor.AddError(StoryParser.ErrorCode.UnknownEntityType, GetArgumentToken(0)?.Span ?? typeName.Span,
                $"'{type}'");

        return Visitor.Database.Types[(int) type.Id];
    }

    public string GetText(TextSpan span) => span.ToStringValue();

    public ScopeNode? GetScopeContext() => CallContext.Call?.Scope ?? CallContext.RawCall?.Scope;

    public void ExpectArgcount(int i, bool isMaxCount = false)
    {
        if (isMaxCount ? ArgCount > i : ArgCount != i)
            Visitor.AddError(StoryParser.ErrorCode.MissingArgument, CallContext.Span,
                $"Expected {i} arguments{(isMaxCount ? " max" : "")}, got {ArgCount}");
    }

    public IValueSql ParsePredicateSql(EntityTypeId entityTypeId)
    {
        // The predicate compiles to SQL: flag user-function calls within it as inlined (not steppable).
        Visitor.InSqlPredicateDepth++;
        IValue v;
        try
        {
            v = ParsePredicate(entityTypeId);
        }
        finally
        {
            Visitor.InSqlPredicateDepth--;
        }

        if (v is IValueSql sql)
            return sql;
        Visitor.AddError(StoryParser.ErrorCode.ExpectedSql, CallContext.Span, "Expected SQL expression");
        return null!;
    }

    public IValue ParsePredicate(EntityTypeId entityTypeId)
    {
        if (ArgCount == 1)
        {
            var only = ParseArgument(0);
            WarnIfRedundantTypeFilter(only, 0, entityTypeId);
            return only;
        }

        IValue[] preds = new IValue[ArgCount];
        for (int i = 0; i < ArgCount; i++)
        {
            preds[i] = ParseArgument(i);
            WarnIfRedundantTypeFilter(preds[i], i, entityTypeId);
        }

        return new And(preds);
    }

    /// <summary>
    /// Flags a <c>type = T</c> filter that repeats the iteration's declared type, e.g.
    /// <c>each Person $p: (type = Person, ...)</c> — the typed <c>each</c>/<c>pick</c> already
    /// constrains to that type, so the filter is always true and can be dropped. Only fires when the
    /// declaration carries an explicit type; the untyped form <c>each $p: (type = Item)</c> (where the
    /// filter is what selects the type) leaves <paramref name="entityTypeId"/> null and is left alone.
    /// </summary>
    private void WarnIfRedundantTypeFilter(IValue pred, int index, EntityTypeId entityTypeId)
    {
        if (entityTypeId != EntityTypeId.Null
            && pred is IsOfType { } iot
            && iot.ValueTypeId == entityTypeId)
        {
            Visitor.AddWarning(StoryParser.ErrorCode.RedundantTypeFilter,
                GetArgumentToken(index)?.Span ?? CallContext.Span,
                $"redundant 'type = {Visitor.Database.GetEntityTypeName(entityTypeId)}' filter; " +
                "the iteration is already constrained to this type by its declaration");
        }
    }
}
