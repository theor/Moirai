using Superpower.Model;

namespace Moirai.Parser.Ast;

public static partial class MoiraiGrammar
{
    internal static TokenList<MoiraiTokenKind> SkipLineBreaksStar(TokenList<MoiraiTokenKind> input)
    {
        var cur = input;
        while (true)
        {
            var t = cur.ConsumeToken();
            if (!t.HasValue || t.Value.Kind != MoiraiTokenKind.LineBreak) break;
            cur = t.Remainder;
        }

        return cur;
    }

    internal static TokenListParserResult<MoiraiTokenKind, Unit> SkipLineBreaksPlus(
        TokenList<MoiraiTokenKind> input)
    {
        var first = input.ConsumeToken();
        if (!first.HasValue || first.Value.Kind != MoiraiTokenKind.LineBreak)
            return TokenListParserResult.Empty<MoiraiTokenKind, Unit>(input, "a line break");
        return TokenListParserResult.Value(Unit.Value, input, SkipLineBreaksStar(first.Remainder));
    }

    /// Shared left-associative binary-operator loop backing every `expr` precedence level (the
    /// Superpower replacement for ANTLR's automatic left-recursion elimination on `expr`). A
    /// `LINE_BREAK` immediately after the operator is optionally consumed before parsing the right
    /// operand, matching the grammar's `op LINE_BREAK? right=expr` — the DSL's only line-continuation
    /// mechanism.
    static TokenListParserResult<MoiraiTokenKind, ExprNode> LeftAssoc(
        TokenList<MoiraiTokenKind> input,
        Func<TokenList<MoiraiTokenKind>, TokenListParserResult<MoiraiTokenKind, ExprNode>> operand,
        params MoiraiTokenKind[] operators)
    {
        var first = operand(input);
        if (!first.HasValue) return first;

        var left = first.Value;
        var remainder = first.Remainder;
        while (true)
        {
            var opTok = remainder.ConsumeToken();
            if (!opTok.HasValue || Array.IndexOf(operators, opTok.Value.Kind) < 0)
                break;

            var afterOp = opTok.Remainder;
            var lb = afterOp.ConsumeToken();
            if (lb.HasValue && lb.Value.Kind == MoiraiTokenKind.LineBreak)
                afterOp = lb.Remainder;

            var right = operand(afterOp);
            if (!right.HasValue)
                return right;

            left = new ExprNode(null, null, null, null, opTok.Value.ToStringValue(), left, right.Value,
                Combine(left.Span, right.Value.Span));
            remainder = right.Remainder;
        }

        return TokenListParserResult.Value(left, input, remainder);
    }

    // expr: if | match | value | (PAREN_OPEN expr PAREN_CLOSE) — the atom/prefix position.
    //
    // allowTrailingScope controls whether a call/raw_call bottomed out here may attach a trailing
    // `scope?` (the `{ ... }` body create/each/pick/schedule use, e.g. `each Person $p: (pred) { ... }`).
    // A `call`'s trailing scope is syntactically optional everywhere per the grammar, which makes it
    // genuinely ambiguous whenever a call sits inside a larger construct that *also* expects a `{`
    // next — most concretely `if COND { ... }`: if COND's own trailing call were allowed to swallow
    // that same `{`, `if`'s own mandatory `then` scope would have nothing left to parse. ANTLR's
    // ALL(*) resolves this with full-context lookahead; this hand-written descent doesn't have that,
    // so instead only the single outermost expression directly under EffectRule's top-level `expr`
    // branch (the one real place a scope-attached call is ever written) is allowed to claim one —
    // every recursive `Expr`/`Value` call elsewhere (if/match conditions, binary operands, set/init/var
    // right-hand sides, call arguments, parenthesized groups, ...) defaults to false.
    static TokenListParserResult<MoiraiTokenKind, ExprNode> ExprAtom(TokenList<MoiraiTokenKind> input,
        bool allowTrailingScope)
    {
        var lead = PeekKind(input);
        if (lead == MoiraiTokenKind.If)
        {
            var r = IfRule(input);
            return r.HasValue
                ? TokenListParserResult.Value(
                    new ExprNode(r.Value, null, null, null, null, null, null, r.Value.Span), input, r.Remainder)
                : TokenListParserResult.CastEmpty<MoiraiTokenKind, IfNode, ExprNode>(r);
        }

        if (lead is MoiraiTokenKind.Match or MoiraiTokenKind.MatchWeight)
        {
            var r = MatchRule(input);
            return r.HasValue
                ? TokenListParserResult.Value(
                    new ExprNode(null, r.Value, null, null, null, null, null, r.Value.Span), input, r.Remainder)
                : TokenListParserResult.CastEmpty<MoiraiTokenKind, MatchNode, ExprNode>(r);
        }

        if (lead == MoiraiTokenKind.ParenOpen)
        {
            var open = input.ConsumeToken();
            var inner = Expr(open.Remainder);
            if (!inner.HasValue)
                return inner;
            var close = inner.Remainder.ConsumeToken();
            if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ParenClose)
                return TokenListParserResult.Empty<MoiraiTokenKind, ExprNode>(inner.Remainder, "')'");
            var span = Combine(open.Value, close.Value);
            return TokenListParserResult.Value(
                new ExprNode(null, null, null, inner.Value, null, null, null, span), input, close.Remainder);
        }

        var v = Value(input, allowTrailingScope);
        return v.HasValue
            ? TokenListParserResult.Value(new ExprNode(null, null, v.Value, null, null, null, null, v.Value.Span),
                input, v.Remainder)
            : TokenListParserResult.CastEmpty<MoiraiTokenKind, ValueNode, ExprNode>(v);
    }

    // Precedence, tightest to loosest: * / % ; + - ; = != >= <= > < ; ?? ; and ; or.
    static TokenListParserResult<MoiraiTokenKind, ExprNode> ExprMulDivMod(TokenList<MoiraiTokenKind> input,
        bool allowTrailingScope) =>
        LeftAssoc(input, i => ExprAtom(i, allowTrailingScope), MoiraiTokenKind.Mul, MoiraiTokenKind.Div,
            MoiraiTokenKind.Mod);

    static TokenListParserResult<MoiraiTokenKind, ExprNode> ExprAddSub(TokenList<MoiraiTokenKind> input,
        bool allowTrailingScope) =>
        LeftAssoc(input, i => ExprMulDivMod(i, allowTrailingScope), MoiraiTokenKind.Add, MoiraiTokenKind.Sub);

    static TokenListParserResult<MoiraiTokenKind, ExprNode> ExprCompare(TokenList<MoiraiTokenKind> input,
        bool allowTrailingScope) =>
        LeftAssoc(input, i => ExprAddSub(i, allowTrailingScope), MoiraiTokenKind.Eq, MoiraiTokenKind.Neq,
            MoiraiTokenKind.Ge, MoiraiTokenKind.Le, MoiraiTokenKind.Gt, MoiraiTokenKind.Lt);

    static TokenListParserResult<MoiraiTokenKind, ExprNode> ExprCoalesce(TokenList<MoiraiTokenKind> input,
        bool allowTrailingScope) =>
        LeftAssoc(input, i => ExprCompare(i, allowTrailingScope), MoiraiTokenKind.Qq);

    static TokenListParserResult<MoiraiTokenKind, ExprNode> ExprAnd(TokenList<MoiraiTokenKind> input,
        bool allowTrailingScope) =>
        LeftAssoc(input, i => ExprCoalesce(i, allowTrailingScope), MoiraiTokenKind.And);

    static TokenListParserResult<MoiraiTokenKind, ExprNode> ExprOr(TokenList<MoiraiTokenKind> input,
        bool allowTrailingScope) =>
        LeftAssoc(input, i => ExprAnd(i, allowTrailingScope), MoiraiTokenKind.Or);

    static TokenListParserResult<MoiraiTokenKind, ExprNode> Expr(TokenList<MoiraiTokenKind> input,
        bool allowTrailingScope = false) =>
        ExprOr(input, allowTrailingScope);

    // if: IF cond=expr then=scope (ELSE LINE_BREAK* else=scope)? ;
    static TokenListParserResult<MoiraiTokenKind, IfNode> IfRule(TokenList<MoiraiTokenKind> input)
    {
        var ifTok = input.ConsumeToken();
        if (!ifTok.HasValue || ifTok.Value.Kind != MoiraiTokenKind.If)
            return TokenListParserResult.Empty<MoiraiTokenKind, IfNode>(input, "'if'");

        var cond = Expr(ifTok.Remainder);
        if (!cond.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, IfNode>(cond);

        var then = Scope(cond.Remainder);
        if (!then.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ScopeNode, IfNode>(then);

        ScopeNode? elseScope = null;
        var remainder = then.Remainder;
        var elseTok = remainder.ConsumeToken();
        if (elseTok.HasValue && elseTok.Value.Kind == MoiraiTokenKind.Else)
        {
            var afterElse = SkipLineBreaksStar(elseTok.Remainder);
            var elseResult = Scope(afterElse);
            if (!elseResult.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, ScopeNode, IfNode>(elseResult);
            elseScope = elseResult.Value;
            remainder = elseResult.Remainder;
        }

        var span = Combine(ifTok.Value.Span, elseScope?.Span ?? then.Value.Span);
        return TokenListParserResult.Value(new IfNode(cond.Value, then.Value, elseScope, span), input, remainder);
    }

    // match: (MATCH|MATCH_WEIGHT) expr (COMMA expr)* SCOPE_OPEN LINE_BREAK* match_case+ SCOPE_CLOSE LINE_BREAK*;
    static TokenListParserResult<MoiraiTokenKind, MatchNode> MatchRule(TokenList<MoiraiTokenKind> input)
    {
        var kw = input.ConsumeToken();
        if (!kw.HasValue || kw.Value.Kind is not (MoiraiTokenKind.Match or MoiraiTokenKind.MatchWeight))
            return TokenListParserResult.Empty<MoiraiTokenKind, MatchNode>(input, "'match' or 'random_weighted'");
        bool isWeight = kw.Value.Kind == MoiraiTokenKind.MatchWeight;

        var exprs = new List<ExprNode>();
        var first = Expr(kw.Remainder);
        if (!first.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, MatchNode>(first);
        exprs.Add(first.Value);
        var remainder = first.Remainder;
        while (PeekKind(remainder) == MoiraiTokenKind.Comma)
        {
            var comma = remainder.ConsumeToken();
            var next = Expr(comma.Remainder);
            if (!next.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, ExprNode, MatchNode>(next);
            exprs.Add(next.Value);
            remainder = next.Remainder;
        }

        var open = remainder.ConsumeToken();
        if (!open.HasValue || open.Value.Kind != MoiraiTokenKind.ScopeOpen)
            return TokenListParserResult.Empty<MoiraiTokenKind, MatchNode>(remainder, "'{'");
        remainder = SkipLineBreaksStar(open.Remainder);

        var cases = new List<MatchCaseNode>();
        var firstCase = MatchCase(remainder);
        if (!firstCase.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, MatchCaseNode, MatchNode>(firstCase);
        cases.Add(firstCase.Value);
        remainder = firstCase.Remainder;
        while (PeekKind(remainder) != MoiraiTokenKind.ScopeClose)
        {
            var c = MatchCase(remainder);
            if (!c.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, MatchCaseNode, MatchNode>(c);
            cases.Add(c.Value);
            remainder = c.Remainder;
        }

        var close = remainder.ConsumeToken();
        if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ScopeClose)
            return TokenListParserResult.Empty<MoiraiTokenKind, MatchNode>(remainder, "'}'");
        remainder = SkipLineBreaksStar(close.Remainder);

        var span = Combine(kw.Value.Span, close.Value.Span);
        return TokenListParserResult.Value(new MatchNode(isWeight, exprs.ToArray(), cases.ToArray(), span), input,
            remainder);
    }

    // match_case: value (COMMA value)* ARROW ((effect LINE_BREAK+)|scope) ;
    static TokenListParserResult<MoiraiTokenKind, MatchCaseNode> MatchCase(TokenList<MoiraiTokenKind> input)
    {
        var values = new List<ValueNode>();
        var first = Value(input);
        if (!first.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ValueNode, MatchCaseNode>(first);
        values.Add(first.Value);
        var remainder = first.Remainder;
        while (PeekKind(remainder) == MoiraiTokenKind.Comma)
        {
            var comma = remainder.ConsumeToken();
            var next = Value(comma.Remainder);
            if (!next.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, ValueNode, MatchCaseNode>(next);
            values.Add(next.Value);
            remainder = next.Remainder;
        }

        var arrow = remainder.ConsumeToken();
        if (!arrow.HasValue || arrow.Value.Kind != MoiraiTokenKind.Arrow)
            return TokenListParserResult.Empty<MoiraiTokenKind, MatchCaseNode>(remainder, "'=>'");

        if (PeekKind(arrow.Remainder) == MoiraiTokenKind.ScopeOpen)
        {
            var scope = Scope(arrow.Remainder);
            if (!scope.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, ScopeNode, MatchCaseNode>(scope);
            var span = Combine(values[0].Span, scope.Value.Span);
            return TokenListParserResult.Value(new MatchCaseNode(values.ToArray(), null, scope.Value, span), input,
                scope.Remainder);
        }

        var effect = EffectRule(arrow.Remainder);
        if (!effect.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, EffectNode, MatchCaseNode>(effect);
        // Grammar requires LINE_BREAK+ here, but (same issue as ScopeBody) an effect that ends in a
        // nested scope-or-match/random_weighted block may have already consumed its own trailing
        // LINE_BREAK* — so this only consumes whatever's left rather than requiring at least one.
        var afterBreaks = SkipLineBreaksStar(effect.Remainder);
        var effectSpan = Combine(values[0].Span, effect.Value.Span);
        return TokenListParserResult.Value(new MatchCaseNode(values.ToArray(), effect.Value, null, effectSpan),
            input, afterBreaks);
    }
}
