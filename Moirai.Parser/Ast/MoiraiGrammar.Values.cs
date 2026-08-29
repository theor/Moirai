using Superpower;
using Superpower.Model;

namespace Moirai.Parser.Ast;

public static partial class MoiraiGrammar
{
    static ValueNode VRawCall(RawCallNode v) => new(v, null, null, null, null, null, null, null, false, v.Span);
    static ValueNode VCall(CallNode v) => new(null, v, null, null, null, null, null, null, false, v.Span);
    static ValueNode VString(StringNode v) => new(null, null, v, null, null, null, null, null, false, v.Span);
    static ValueNode VEnum(EnumValueNode v) => new(null, null, null, v, null, null, null, null, false, v.Span);
    static ValueNode VTypeId(Ident v) => new(null, null, null, null, v, null, null, null, false, v.Span);
    static ValueNode VPath(PathNode v) => new(null, null, null, null, null, v, null, null, false, v.Span);
    static ValueNode VBool(bool v, TextSpan span) => new(null, null, null, null, null, null, v, null, false, span);
    static ValueNode VNumber(NumberNode v) => new(null, null, null, null, null, null, null, v, false, v.Span);
    static ValueNode VNull(TextSpan span) => new(null, null, null, null, null, null, null, null, true, span);

    // string: QUOTE stringContent* QUOTE ;   stringContent: (EXPR_OPEN expr SCOPE_CLOSE) | TEXT ;
    static TokenListParserResult<MoiraiTokenKind, StringNode> StringRule(TokenList<MoiraiTokenKind> input)
    {
        var open = input.ConsumeToken();
        if (!open.HasValue || open.Value.Kind != MoiraiTokenKind.Quote)
            return TokenListParserResult.Empty<MoiraiTokenKind, StringNode>(input, "a string");

        var parts = new List<StringPartNode>();
        var remainder = open.Remainder;
        while (true)
        {
            var next = PeekKind(remainder);
            if (next == MoiraiTokenKind.Quote)
                break;

            if (next == MoiraiTokenKind.Text)
            {
                var t = remainder.ConsumeToken();
                parts.Add(new StringTextPart(t.Value.ToStringValue(), t.Value.Span));
                remainder = t.Remainder;
                continue;
            }

            if (next == MoiraiTokenKind.ExprOpen)
            {
                var eo = remainder.ConsumeToken();
                var exprResult = Expr(eo.Remainder);
                if (!exprResult.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, StringNode>(exprResult);
                var close = exprResult.Remainder.ConsumeToken();
                if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ScopeClose)
                    return TokenListParserResult.Empty<MoiraiTokenKind, StringNode>(exprResult.Remainder, "'}'");
                parts.Add(new StringExprPart(exprResult.Value, Combine(eo.Value, close.Value)));
                remainder = close.Remainder;
                continue;
            }

            return TokenListParserResult.Empty<MoiraiTokenKind, StringNode>(remainder,
                "string content or closing quote");
        }

        var closeQuote = remainder.ConsumeToken();
        return TokenListParserResult.Value(
            new StringNode(parts.ToArray(), Combine(open.Value, closeQuote.Value)), input, closeQuote.Remainder);
    }

    static TokenListParserResult<MoiraiTokenKind, ScopeNode?> ScopeOptional(TokenList<MoiraiTokenKind> input)
    {
        if (PeekKind(input) != MoiraiTokenKind.ScopeOpen)
            return TokenListParserResult.Value((ScopeNode?) null, input, input);
        var s = Scope(input);
        return s.HasValue
            ? TokenListParserResult.Value((ScopeNode?) s.Value, input, s.Remainder)
            : TokenListParserResult.CastEmpty<MoiraiTokenKind, ScopeNode, ScopeNode?>(s);
    }

    /// Tries "type VAR_ID" starting at `input` (the shared prefix of call's and raw_call's typed
    /// declaration form, e.g. `pick Person $p: (...)` / `create Person $p`). Returns false — with
    /// `input` untouched — if it doesn't match, so the caller can fall back to other alternatives;
    /// this is what makes `type: TYPE_ID | ID | ...` accepting a bare lowercase ID safe: "each item
    /// $x: (...)" only commits to reading "item" as a type name once a VAR_ID is confirmed right
    /// after it, otherwise "item" is reinterpreted as the start of something else entirely.
    static bool TryTypedDeclPrefix(TokenList<MoiraiTokenKind> input, out TypeNode type, out Ident varId,
        out TokenList<MoiraiTokenKind> remainder)
    {
        type = default!;
        varId = default;
        remainder = input;
        var lead = PeekKind(input);
        if (lead is not (MoiraiTokenKind.TypeId or MoiraiTokenKind.Id or MoiraiTokenKind.LBrack))
            return false;

        var typeResult = TypeRule(input);
        if (!typeResult.HasValue)
            return false;

        var varTok = typeResult.Remainder.ConsumeToken();
        if (!varTok.HasValue || varTok.Value.Kind != MoiraiTokenKind.VarId)
            return false;

        type = typeResult.Value;
        varId = IdentOf(varTok.Value);
        remainder = varTok.Remainder;
        return true;
    }

    // call : fun_id (type VAR_ID COLON)? PAREN_OPEN (expr (COMMA expr)*)? PAREN_CLOSE scope? ;
    static TokenListParserResult<MoiraiTokenKind, CallNode> CallTail(TokenList<MoiraiTokenKind> original,
        Ident funId, TypeNode? declType, Ident? declVarId, TokenList<MoiraiTokenKind> atParen)
    {
        var open = atParen.ConsumeToken(); // guaranteed ParenOpen by the caller's peek
        var args = new List<ExprNode>();
        var remainder = open.Remainder;
        if (PeekKind(remainder) != MoiraiTokenKind.ParenClose)
        {
            var first = Expr(remainder);
            if (!first.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, CallNode>(first);
            args.Add(first.Value);
            remainder = first.Remainder;
            while (PeekKind(remainder) == MoiraiTokenKind.Comma)
            {
                var comma = remainder.ConsumeToken();
                var next = Expr(comma.Remainder);
                if (!next.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, CallNode>(next);
                args.Add(next.Value);
                remainder = next.Remainder;
            }
        }

        var close = remainder.ConsumeToken();
        if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ParenClose)
            return TokenListParserResult.Empty<MoiraiTokenKind, CallNode>(remainder, "')'");

        var scopeResult = ScopeOptional(close.Remainder);
        if (!scopeResult.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ScopeNode?, CallNode>(scopeResult);

        var endSpan = scopeResult.Value?.Span ?? close.Value.Span;
        var node = new CallNode(funId, declType, declVarId, args.ToArray(), scopeResult.Value,
            Combine(funId.Span, endSpan));
        return TokenListParserResult.Value(node, original, scopeResult.Remainder);
    }

    /// Dispatches the ID-leading alternatives of `value` (raw_call | call | path — the three
    /// productions that can start with a plain lowercase identifier). Tries, in order: a typed
    /// declaration prefix (shared by call/raw_call), then '(' for `call`, then a bare `value` for
    /// raw_call's un-parenthesized form (`record 'text'`), and only falls back to a bare `path`
    /// (just the identifier, or the start of a property chain) once every call/raw_call shape has
    /// been ruled out — matching how ANTLR's own grammar only accepts `path` here once neither
    /// call form applies. See MoiraiGrammar's type doc for why this can't be plain Or/Try chaining.
    static TokenListParserResult<MoiraiTokenKind, ValueNode> IdLeadingValue(TokenList<MoiraiTokenKind> input)
    {
        var funIdResult = FunId(input);
        if (!funIdResult.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, ValueNode>(funIdResult);

        var funId = funIdResult.Value;
        var afterFunId = funIdResult.Remainder;
        bool hasDecl = TryTypedDeclPrefix(afterFunId, out var declType, out var declVarId, out var afterDecl);

        if (!hasDecl && PeekKind(afterFunId) == MoiraiTokenKind.ParenOpen)
        {
            // call, no type/var decl: fun_id PAREN_OPEN ...
            var call = CallTail(input, funId, null, null, afterFunId);
            return call.HasValue
                ? TokenListParserResult.Value(VCall(call.Value), input, call.Remainder)
                : TokenListParserResult.CastEmpty<MoiraiTokenKind, CallNode, ValueNode>(call);
        }

        if (hasDecl)
        {
            // Both call's `type VAR_ID COLON PAREN_OPEN ...` and raw_call's `type VAR_ID (COLON
            // value)?` share the "type VAR_ID" prefix (already consumed into afterDecl) and can
            // both continue with COLON — only a PAREN_OPEN right after that colon means `call`;
            // anything else after the colon (or no colon at all) is raw_call's typed-decl form.
            if (PeekKind(afterDecl) == MoiraiTokenKind.Colon &&
                PeekKindAt(afterDecl, 1) == MoiraiTokenKind.ParenOpen)
            {
                var colon = afterDecl.ConsumeToken();
                var call = CallTail(input, funId, declType, declVarId, colon.Remainder);
                return call.HasValue
                    ? TokenListParserResult.Value(VCall(call.Value), input, call.Remainder)
                    : TokenListParserResult.CastEmpty<MoiraiTokenKind, CallNode, ValueNode>(call);
            }

            // raw_call typed-decl form: fun_id type VAR_ID (COLON value)? scope?
            ValueNode? declValue = null;
            var afterColon = afterDecl;
            if (PeekKind(afterDecl) == MoiraiTokenKind.Colon)
            {
                var colon = afterDecl.ConsumeToken();
                var v = Value(colon.Remainder);
                if (!v.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, ValueNode, ValueNode>(v);
                declValue = v.Value;
                afterColon = v.Remainder;
            }

            var scopeResult = ScopeOptional(afterColon);
            if (!scopeResult.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, ScopeNode?, ValueNode>(scopeResult);

            var endSpan = scopeResult.Value?.Span ?? declValue?.Span ?? declVarId.Span;
            var rawCall = new RawCallNode(funId, declType, declVarId, declValue, scopeResult.Value,
                Combine(funId.Span, endSpan));
            return TokenListParserResult.Value(VRawCall(rawCall), input, scopeResult.Remainder);
        }

        // No '(' and no typed-decl: try raw_call's bare-value form (fun_id value scope?).
        var bareValue = Value(afterFunId);
        if (bareValue.HasValue)
        {
            var scopeResult = ScopeOptional(bareValue.Remainder);
            if (!scopeResult.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, ScopeNode?, ValueNode>(scopeResult);

            var endSpan = scopeResult.Value?.Span ?? bareValue.Value.Span;
            var rawCall = new RawCallNode(funId, null, null, bareValue.Value, scopeResult.Value,
                Combine(funId.Span, endSpan));
            return TokenListParserResult.Value(VRawCall(rawCall), input, scopeResult.Remainder);
        }

        // Neither call, raw_call's typed form, nor raw_call's bare-value form matched: this
        // identifier (and any .dot chain after it) is just a path — re-parse from the very start,
        // not from wherever the failed attempts above left off.
        var path = Path(input);
        return path.HasValue
            ? TokenListParserResult.Value(VPath(path.Value), input, path.Remainder)
            : TokenListParserResult.CastEmpty<MoiraiTokenKind, PathNode, ValueNode>(path);
    }

    // value: raw_call | call | string | enum_value | type_id | path | bool | number | NULL ;
    static TokenListParserResult<MoiraiTokenKind, ValueNode> Value(TokenList<MoiraiTokenKind> input)
    {
        var lead = PeekKind(input);
        switch (lead)
        {
            case MoiraiTokenKind.Quote:
            {
                var s = StringRule(input);
                return s.HasValue
                    ? TokenListParserResult.Value(VString(s.Value), input, s.Remainder)
                    : TokenListParserResult.CastEmpty<MoiraiTokenKind, StringNode, ValueNode>(s);
            }
            case MoiraiTokenKind.Null:
            {
                var t = input.ConsumeToken();
                return TokenListParserResult.Value(VNull(t.Value.Span), input, t.Remainder);
            }
            case MoiraiTokenKind.True:
            case MoiraiTokenKind.False:
            {
                var b = BoolRule(input);
                return b.HasValue
                    ? TokenListParserResult.Value(VBool(b.Value.Value, b.Value.Span), input, b.Remainder)
                    : TokenListParserResult.CastEmpty<MoiraiTokenKind, (bool, TextSpan), ValueNode>(b);
            }
            case MoiraiTokenKind.Number:
            case MoiraiTokenKind.NumberFloat:
            case MoiraiTokenKind.Percent:
            {
                var n = NumberRule(input);
                return n.HasValue
                    ? TokenListParserResult.Value(VNumber(n.Value), input, n.Remainder)
                    : TokenListParserResult.CastEmpty<MoiraiTokenKind, NumberNode, ValueNode>(n);
            }
            case MoiraiTokenKind.SingletonId:
            case MoiraiTokenKind.VarId:
            {
                var p = Path(input);
                return p.HasValue
                    ? TokenListParserResult.Value(VPath(p.Value), input, p.Remainder)
                    : TokenListParserResult.CastEmpty<MoiraiTokenKind, PathNode, ValueNode>(p);
            }
            case MoiraiTokenKind.Id:
                return IdLeadingValue(input);
            case MoiraiTokenKind.TypeId:
            {
                if (PeekKindAt(input, 1) == MoiraiTokenKind.Dot)
                {
                    var ev = EnumValueRule(input);
                    return ev.HasValue
                        ? TokenListParserResult.Value(VEnum(ev.Value), input, ev.Remainder)
                        : TokenListParserResult.CastEmpty<MoiraiTokenKind, EnumValueNode, ValueNode>(ev);
                }

                var tid = TypeId(input);
                return tid.HasValue
                    ? TokenListParserResult.Value(VTypeId(tid.Value), input, tid.Remainder)
                    : TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, ValueNode>(tid);
            }
            default:
                return TokenListParserResult.Empty<MoiraiTokenKind, ValueNode>(input, "a value");
        }
    }

    // Top-level `call` (only reachable directly — as opposed to via `value`'s ID dispatch — from
    // dot_property: DOT call, e.g. `$e.foo()`).
    static TokenListParserResult<MoiraiTokenKind, CallNode> Call(TokenList<MoiraiTokenKind> input)
    {
        var funIdResult = FunId(input);
        if (!funIdResult.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, CallNode>(funIdResult);

        var afterFunId = funIdResult.Remainder;
        bool hasDecl = TryTypedDeclPrefix(afterFunId, out var declType, out var declVarId, out var afterDecl);

        if (!hasDecl)
        {
            if (PeekKind(afterFunId) != MoiraiTokenKind.ParenOpen)
                return TokenListParserResult.Empty<MoiraiTokenKind, CallNode>(afterFunId, "'('");
            return CallTail(input, funIdResult.Value, null, null, afterFunId);
        }

        // type VAR_ID COLON PAREN_OPEN ... — call's typed-decl form requires the colon.
        if (PeekKind(afterDecl) != MoiraiTokenKind.Colon || PeekKindAt(afterDecl, 1) != MoiraiTokenKind.ParenOpen)
            return TokenListParserResult.Empty<MoiraiTokenKind, CallNode>(afterDecl, "':' followed by '('");
        var colonTok = afterDecl.ConsumeToken();
        return CallTail(input, funIdResult.Value, declType, declVarId, colonTok.Remainder);
    }
}
