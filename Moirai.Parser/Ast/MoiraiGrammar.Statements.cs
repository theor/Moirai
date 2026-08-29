using Superpower.Model;

namespace Moirai.Parser.Ast;

public static partial class MoiraiGrammar
{
    // effect: (set | init | var | expr) SPACE* (LINE_BREAK)* ;
    // NOTE: departs from a literal port in one deliberate way — the grammar lets `effect` itself
    // swallow trailing LINE_BREAK* (SPACE* is always a no-op: SPACE is filtered to trivia before the
    // parser ever sees it, exactly like ANTLR routing it to the HIDDEN channel). Here, LINE_BREAK
    // consumption between statements is handled uniformly by ScopeBody instead, so EffectNode's span
    // covers only its set/init/var/expr content. This changes ANTLR's rule-span boundaries (which the
    // FileRangePositionTests pinning test already showed extend into trailing separators) but not
    // what programs are accepted — a Phase 3 concern (debug-span fidelity) to revisit deliberately,
    // not an accidental one to inherit here.
    static TokenListParserResult<MoiraiTokenKind, EffectNode> EffectRule(TokenList<MoiraiTokenKind> input)
    {
        var lead = PeekKind(input);
        if (lead == MoiraiTokenKind.Set)
        {
            var r = SetRule(input);
            return r.HasValue
                ? TokenListParserResult.Value(new EffectNode(null, null, r.Value, null, r.Value.Span), input,
                    r.Remainder)
                : TokenListParserResult.CastEmpty<MoiraiTokenKind, SetNode, EffectNode>(r);
        }

        if (lead == MoiraiTokenKind.Var)
        {
            var r = VarRule(input);
            return r.HasValue
                ? TokenListParserResult.Value(new EffectNode(null, r.Value, null, null, r.Value.Span), input,
                    r.Remainder)
                : TokenListParserResult.CastEmpty<MoiraiTokenKind, VarNode, EffectNode>(r);
        }

        // init: property_id COLON_EQ expr — shares its leading ID with expr's path/raw_call
        // alternatives; 2-token lookahead (ID then COLON_EQ) disambiguates since nothing else in
        // expr's grammar puts COLON_EQ directly after a lone ID.
        if (lead == MoiraiTokenKind.Id && PeekKindAt(input, 1) == MoiraiTokenKind.ColonEq)
        {
            var r = InitRule(input);
            return r.HasValue
                ? TokenListParserResult.Value(new EffectNode(null, null, null, r.Value, r.Value.Span), input,
                    r.Remainder)
                : TokenListParserResult.CastEmpty<MoiraiTokenKind, InitNode, EffectNode>(r);
        }

        var expr = Expr(input);
        return expr.HasValue
            ? TokenListParserResult.Value(new EffectNode(expr.Value, null, null, null, expr.Value.Span), input,
                expr.Remainder)
            : TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, EffectNode>(expr);
    }

    // set: SET path EQ expr ;
    static TokenListParserResult<MoiraiTokenKind, SetNode> SetRule(TokenList<MoiraiTokenKind> input)
    {
        var setTok = input.ConsumeToken();
        if (!setTok.HasValue || setTok.Value.Kind != MoiraiTokenKind.Set)
            return TokenListParserResult.Empty<MoiraiTokenKind, SetNode>(input, "'set'");
        var path = Path(setTok.Remainder);
        if (!path.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, PathNode, SetNode>(path);
        var eq = path.Remainder.ConsumeToken();
        if (!eq.HasValue || eq.Value.Kind != MoiraiTokenKind.Eq)
            return TokenListParserResult.Empty<MoiraiTokenKind, SetNode>(path.Remainder, "'='");
        var expr = Expr(eq.Remainder);
        if (!expr.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, SetNode>(expr);
        var span = Combine(setTok.Value.Span, expr.Value.Span);
        return TokenListParserResult.Value(new SetNode(path.Value, expr.Value, span), input, expr.Remainder);
    }

    // init: property_id COLON_EQ expr ;
    static TokenListParserResult<MoiraiTokenKind, InitNode> InitRule(TokenList<MoiraiTokenKind> input)
    {
        var prop = PropertyId(input);
        if (!prop.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, InitNode>(prop);
        var colonEq = prop.Remainder.ConsumeToken();
        if (!colonEq.HasValue || colonEq.Value.Kind != MoiraiTokenKind.ColonEq)
            return TokenListParserResult.Empty<MoiraiTokenKind, InitNode>(prop.Remainder, "':='");
        var expr = Expr(colonEq.Remainder);
        if (!expr.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, InitNode>(expr);
        var span = Combine(prop.Value.Span, expr.Value.Span);
        return TokenListParserResult.Value(new InitNode(prop.Value, expr.Value, span), input, expr.Remainder);
    }

    // var: VAR VAR_ID COLON expr ;
    static TokenListParserResult<MoiraiTokenKind, VarNode> VarRule(TokenList<MoiraiTokenKind> input)
    {
        var varTok = input.ConsumeToken();
        if (!varTok.HasValue || varTok.Value.Kind != MoiraiTokenKind.Var)
            return TokenListParserResult.Empty<MoiraiTokenKind, VarNode>(input, "'var'");
        var id = varTok.Remainder.ConsumeToken();
        if (!id.HasValue || id.Value.Kind != MoiraiTokenKind.VarId)
            return TokenListParserResult.Empty<MoiraiTokenKind, VarNode>(varTok.Remainder, "a variable name");
        var colon = id.Remainder.ConsumeToken();
        if (!colon.HasValue || colon.Value.Kind != MoiraiTokenKind.Colon)
            return TokenListParserResult.Empty<MoiraiTokenKind, VarNode>(id.Remainder, "':'");
        var expr = Expr(colon.Remainder);
        if (!expr.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, VarNode>(expr);
        var span = Combine(varTok.Value.Span, expr.Value.Span);
        return TokenListParserResult.Value(new VarNode(IdentOf(id.Value), expr.Value, span), input, expr.Remainder);
    }

    /// The `((effect SCOPE_CLOSE)|((effect LINE_BREAK+)* SCOPE_CLOSE))` alternation from `scope`,
    /// restated as a loop: parse effects until '}', consuming any line breaks between them.
    /// Deliberately doesn't enforce "at least one LINE_BREAK between non-trailing effects": an
    /// effect that itself ends in a nested `scope` (e.g. `create T $x: '...' { prop := 1 }`) already
    /// consumes its own trailing LINE_BREAK* per the `scope` rule, so by the time control returns
    /// here there may be nothing left to see between it and the next effect even though a real
    /// separator existed in the source — enforcing strict alternation would misclassify that as the
    /// "final trailing effect, no break needed" case and reject anything after it. Real .sg sources
    /// always put one statement per line regardless, so this is a harmless superset, not a fidelity
    /// loss worth chasing in Phase 2 (see the plan's Phase 4 for where stricter recovery could
    /// revisit this if it ever matters).
    static TokenListParserResult<MoiraiTokenKind, EffectNode[]> ScopeBody(TokenList<MoiraiTokenKind> input)
    {
        var effects = new List<EffectNode>();
        var remainder = input;
        while (PeekKind(remainder) != MoiraiTokenKind.ScopeClose)
        {
            var effect = EffectRule(remainder);
            if (!effect.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, EffectNode, EffectNode[]>(effect);
            effects.Add(effect.Value);
            remainder = SkipLineBreaksStar(effect.Remainder);
        }

        return TokenListParserResult.Value(effects.ToArray(), input, remainder);
    }

    // scope: SCOPE_OPEN LINE_BREAK* (when|when_created)? <body> SCOPE_CLOSE LINE_BREAK* ;
    static TokenListParserResult<MoiraiTokenKind, ScopeNode> Scope(TokenList<MoiraiTokenKind> input)
    {
        var open = input.ConsumeToken();
        if (!open.HasValue || open.Value.Kind != MoiraiTokenKind.ScopeOpen)
            return TokenListParserResult.Empty<MoiraiTokenKind, ScopeNode>(input, "'{'");
        var remainder = SkipLineBreaksStar(open.Remainder);

        WhenNode? when = null;
        WhenCreatedNode? whenCreated = null;
        var lead = PeekKind(remainder);
        if (lead == MoiraiTokenKind.When)
        {
            var r = WhenRule(remainder);
            if (!r.HasValue) return TokenListParserResult.CastEmpty<MoiraiTokenKind, WhenNode, ScopeNode>(r);
            when = r.Value;
            remainder = r.Remainder;
        }
        else if (lead == MoiraiTokenKind.WhenCreated)
        {
            var r = WhenCreatedRule(remainder);
            if (!r.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, WhenCreatedNode, ScopeNode>(r);
            whenCreated = r.Value;
            remainder = r.Remainder;
        }

        var body = ScopeBody(remainder);
        if (!body.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, EffectNode[], ScopeNode>(body);
        remainder = body.Remainder;

        var close = remainder.ConsumeToken();
        if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ScopeClose)
            return TokenListParserResult.Empty<MoiraiTokenKind, ScopeNode>(remainder, "'}'");
        remainder = SkipLineBreaksStar(close.Remainder);

        var span = Combine(open.Value.Span, close.Value.Span);
        return TokenListParserResult.Value(new ScopeNode(when, whenCreated, body.Value, span), input, remainder);
    }

    // when: WHEN type_id (AND expr)* SPACE* LINE_BREAK+ ;
    static TokenListParserResult<MoiraiTokenKind, WhenNode> WhenRule(TokenList<MoiraiTokenKind> input)
    {
        var kw = input.ConsumeToken();
        if (!kw.HasValue || kw.Value.Kind != MoiraiTokenKind.When)
            return TokenListParserResult.Empty<MoiraiTokenKind, WhenNode>(input, "'when'");
        var typeId = TypeId(kw.Remainder);
        if (!typeId.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, WhenNode>(typeId);

        var exprs = new List<ExprNode>();
        var remainder = typeId.Remainder;
        while (PeekKind(remainder) == MoiraiTokenKind.And)
        {
            var and = remainder.ConsumeToken();
            var e = Expr(and.Remainder);
            if (!e.HasValue) return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, WhenNode>(e);
            exprs.Add(e.Value);
            remainder = e.Remainder;
        }

        var lb = SkipLineBreaksPlus(remainder);
        if (!lb.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Unit, WhenNode>(lb);

        var endSpan = exprs.Count > 0 ? exprs[^1].Span : typeId.Value.Span;
        var span = Combine(kw.Value.Span, endSpan);
        return TokenListParserResult.Value(new WhenNode(IdentOf(kw.Value), typeId.Value, exprs.ToArray(), span),
            input, lb.Remainder);
    }

    // when_created: WHEN_CREATED type_id (AND expr)* SPACE* LINE_BREAK+ ;
    static TokenListParserResult<MoiraiTokenKind, WhenCreatedNode> WhenCreatedRule(
        TokenList<MoiraiTokenKind> input)
    {
        var kw = input.ConsumeToken();
        if (!kw.HasValue || kw.Value.Kind != MoiraiTokenKind.WhenCreated)
            return TokenListParserResult.Empty<MoiraiTokenKind, WhenCreatedNode>(input, "'when_created'");
        var typeId = TypeId(kw.Remainder);
        if (!typeId.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, WhenCreatedNode>(typeId);

        var exprs = new List<ExprNode>();
        var remainder = typeId.Remainder;
        while (PeekKind(remainder) == MoiraiTokenKind.And)
        {
            var and = remainder.ConsumeToken();
            var e = Expr(and.Remainder);
            if (!e.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, WhenCreatedNode>(e);
            exprs.Add(e.Value);
            remainder = e.Remainder;
        }

        var lb = SkipLineBreaksPlus(remainder);
        if (!lb.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Unit, WhenCreatedNode>(lb);

        var endSpan = exprs.Count > 0 ? exprs[^1].Span : typeId.Value.Span;
        var span = Combine(kw.Value.Span, endSpan);
        return TokenListParserResult.Value(
            new WhenCreatedNode(IdentOf(kw.Value), typeId.Value, exprs.ToArray(), span), input, lb.Remainder);
    }

    // event: EVENT ID (PAREN_OPEN (param (COMMA param)*)? PAREN_CLOSE)? scope ;
    static TokenListParserResult<MoiraiTokenKind, EventNode> EventRule(TokenList<MoiraiTokenKind> input)
    {
        var kw = input.ConsumeToken();
        if (!kw.HasValue || kw.Value.Kind != MoiraiTokenKind.Event)
            return TokenListParserResult.Empty<MoiraiTokenKind, EventNode>(input, "'event'");
        var name = kw.Remainder.ConsumeToken();
        if (!name.HasValue || name.Value.Kind != MoiraiTokenKind.Id)
            return TokenListParserResult.Empty<MoiraiTokenKind, EventNode>(kw.Remainder, "an event name");

        var remainder = name.Remainder;
        var parameters = new List<ParamNode>();
        if (PeekKind(remainder) == MoiraiTokenKind.ParenOpen)
        {
            var open = remainder.ConsumeToken();
            remainder = open.Remainder;
            if (PeekKind(remainder) != MoiraiTokenKind.ParenClose)
            {
                var p = ParamRule(remainder);
                if (!p.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, ParamNode, EventNode>(p);
                parameters.Add(p.Value);
                remainder = p.Remainder;
                while (PeekKind(remainder) == MoiraiTokenKind.Comma)
                {
                    var comma = remainder.ConsumeToken();
                    var p2 = ParamRule(comma.Remainder);
                    if (!p2.HasValue)
                        return TokenListParserResult.CastEmpty<MoiraiTokenKind, ParamNode, EventNode>(p2);
                    parameters.Add(p2.Value);
                    remainder = p2.Remainder;
                }
            }

            var close = remainder.ConsumeToken();
            if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ParenClose)
                return TokenListParserResult.Empty<MoiraiTokenKind, EventNode>(remainder, "')'");
            remainder = close.Remainder;
        }

        var scope = Scope(remainder);
        if (!scope.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ScopeNode, EventNode>(scope);
        var span = Combine(kw.Value.Span, scope.Value.Span);
        return TokenListParserResult.Value(
            new EventNode(IdentOf(name.Value), parameters.ToArray(), scope.Value, span), input, scope.Remainder);
    }

    // trigger: TRIGGER ID scope ;
    static TokenListParserResult<MoiraiTokenKind, TriggerNode> TriggerRule(TokenList<MoiraiTokenKind> input)
    {
        var kw = input.ConsumeToken();
        if (!kw.HasValue || kw.Value.Kind != MoiraiTokenKind.Trigger)
            return TokenListParserResult.Empty<MoiraiTokenKind, TriggerNode>(input, "'trigger'");
        var name = kw.Remainder.ConsumeToken();
        if (!name.HasValue || name.Value.Kind != MoiraiTokenKind.Id)
            return TokenListParserResult.Empty<MoiraiTokenKind, TriggerNode>(kw.Remainder, "a trigger name");
        var scope = Scope(name.Remainder);
        if (!scope.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ScopeNode, TriggerNode>(scope);
        var span = Combine(kw.Value.Span, scope.Value.Span);
        return TokenListParserResult.Value(new TriggerNode(IdentOf(name.Value), scope.Value, span), input,
            scope.Remainder);
    }

    // attribute: AT attr=ID (PAREN_OPEN expr (COMMA expr)* PAREN_CLOSE)? LINE_BREAK ;
    static TokenListParserResult<MoiraiTokenKind, AttributeNode> AttributeRule(TokenList<MoiraiTokenKind> input)
    {
        var at = input.ConsumeToken();
        if (!at.HasValue || at.Value.Kind != MoiraiTokenKind.At)
            return TokenListParserResult.Empty<MoiraiTokenKind, AttributeNode>(input, "'@'");
        var name = at.Remainder.ConsumeToken();
        if (!name.HasValue || name.Value.Kind != MoiraiTokenKind.Id)
            return TokenListParserResult.Empty<MoiraiTokenKind, AttributeNode>(at.Remainder, "an attribute name");

        var remainder = name.Remainder;
        var args = new List<ExprNode>();
        if (PeekKind(remainder) == MoiraiTokenKind.ParenOpen)
        {
            var open = remainder.ConsumeToken();
            var first = Expr(open.Remainder);
            if (!first.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, AttributeNode>(first);
            args.Add(first.Value);
            remainder = first.Remainder;
            while (PeekKind(remainder) == MoiraiTokenKind.Comma)
            {
                var comma = remainder.ConsumeToken();
                var next = Expr(comma.Remainder);
                if (!next.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, AttributeNode>(next);
                args.Add(next.Value);
                remainder = next.Remainder;
            }

            var close = remainder.ConsumeToken();
            if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ParenClose)
                return TokenListParserResult.Empty<MoiraiTokenKind, AttributeNode>(remainder, "')'");
            remainder = close.Remainder;
        }

        var lb = remainder.ConsumeToken();
        if (!lb.HasValue || lb.Value.Kind != MoiraiTokenKind.LineBreak)
            return TokenListParserResult.Empty<MoiraiTokenKind, AttributeNode>(remainder, "a line break");

        var span = Combine(at.Value.Span, name.Value.Span);
        return TokenListParserResult.Value(new AttributeNode(IdentOf(name.Value), args.ToArray(), span), input,
            lb.Remainder);
    }

    // def: attribute* (event|trigger|enum_definition|type_definition|function_definition|table_definition) ;
    static TokenListParserResult<MoiraiTokenKind, DefNode> DefRule(TokenList<MoiraiTokenKind> input)
    {
        var attrs = new List<AttributeNode>();
        var remainder = input;
        while (PeekKind(remainder) == MoiraiTokenKind.At)
        {
            var a = AttributeRule(remainder);
            if (!a.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, AttributeNode, DefNode>(a);
            attrs.Add(a.Value);
            remainder = a.Remainder;
        }

        EventNode? evt = null;
        TriggerNode? trig = null;
        EnumDefinitionNode? enumDef = null;
        TypeDefinitionNode? typeDef = null;
        FunctionDefinitionNode? funcDef = null;
        TableDefinitionNode? tableDef = null;
        TextSpan bodySpan;

        switch (PeekKind(remainder))
        {
            case MoiraiTokenKind.Event:
            {
                var r = EventRule(remainder);
                if (!r.HasValue) return TokenListParserResult.CastEmpty<MoiraiTokenKind, EventNode, DefNode>(r);
                evt = r.Value;
                bodySpan = r.Value.Span;
                remainder = r.Remainder;
                break;
            }
            case MoiraiTokenKind.Trigger:
            {
                var r = TriggerRule(remainder);
                if (!r.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, TriggerNode, DefNode>(r);
                trig = r.Value;
                bodySpan = r.Value.Span;
                remainder = r.Remainder;
                break;
            }
            case MoiraiTokenKind.Enum:
            {
                var r = EnumDefinitionRule(remainder);
                if (!r.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, EnumDefinitionNode, DefNode>(r);
                enumDef = r.Value;
                bodySpan = r.Value.Span;
                remainder = r.Remainder;
                break;
            }
            case MoiraiTokenKind.Entity:
            case MoiraiTokenKind.Singleton:
            {
                var r = TypeDefinitionRule(remainder);
                if (!r.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, TypeDefinitionNode, DefNode>(r);
                typeDef = r.Value;
                bodySpan = r.Value.Span;
                remainder = r.Remainder;
                break;
            }
            case MoiraiTokenKind.Function:
            {
                var r = FunctionDefinitionRule(remainder);
                if (!r.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, FunctionDefinitionNode, DefNode>(r);
                funcDef = r.Value;
                bodySpan = r.Value.Span;
                remainder = r.Remainder;
                break;
            }
            case MoiraiTokenKind.Table:
            {
                var r = TableDefinitionRule(remainder);
                if (!r.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, TableDefinitionNode, DefNode>(r);
                tableDef = r.Value;
                bodySpan = r.Value.Span;
                remainder = r.Remainder;
                break;
            }
            default:
                return TokenListParserResult.Empty<MoiraiTokenKind, DefNode>(remainder,
                    "'event', 'trigger', 'enum', 'entity', 'singleton', 'function', or 'table'");
        }

        var overallSpan = attrs.Count > 0 ? Combine(attrs[0].Span, bodySpan) : bodySpan;
        var node = new DefNode(attrs.ToArray(), evt, trig, enumDef, typeDef, funcDef, tableDef, overallSpan);
        return TokenListParserResult.Value(node, input, remainder);
    }

    // r: (def|LINE_BREAK)+ EOF ;  (EOF is implicit — Superpower TokenLists have no explicit EOF
    // token; a successful match here always leaves `remainder.IsAtEnd == true`.)
    static TokenListParserResult<MoiraiTokenKind, RNode> R(TokenList<MoiraiTokenKind> input)
    {
        var defs = new List<DefNode>();
        var remainder = input;
        TextSpan? first = null, last = null;
        while (!remainder.IsAtEnd)
        {
            if (PeekKind(remainder) == MoiraiTokenKind.LineBreak)
            {
                var t = remainder.ConsumeToken();
                first ??= t.Value.Span;
                last = t.Value.Span;
                remainder = t.Remainder;
                continue;
            }

            var d = DefRule(remainder);
            if (!d.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, DefNode, RNode>(d);
            defs.Add(d.Value);
            first ??= d.Value.Span;
            last = d.Value.Span;
            remainder = d.Remainder;
        }

        if (first == null)
            return TokenListParserResult.Empty<MoiraiTokenKind, RNode>(input, "at least one definition");

        return TokenListParserResult.Value(new RNode(defs.ToArray(), Combine(first.Value, last!.Value)), input,
            remainder);
    }
}
