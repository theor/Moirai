using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Moirai.Parser.Ast;

public readonly record struct GrammarError(string Message, Position Position, string[] Expectations);

/// Superpower TokenListParser&lt;MoiraiTokenKind,T&gt; combinators replacing MoiraiParser.g4 (Phase 2
/// of the ANTLR->Superpower migration; see the plan at
/// C:\Users\theor\.claude\plans\stateful-dancing-stroustrup.md). One method per grammar rule, kept
/// close to the .g4 structure so the diff against the frozen grammar stays auditable rule-by-rule.
///
/// Two structural departures from a literal port, per the plan:
///  - Alternatives sharing a leading token (`value: raw_call | call | string | ...`) are resolved by
///    single-token dispatch (peek the next token, branch), not by chained Or/Try — Superpower's Or
///    only backtracks cleanly when the failing branch consumed no input.
///  - The left-recursive `expr` rule becomes a hand-written precedence-climbing loop (see
///    <see cref="BinaryLevel"/>), since Superpower has no left-recursion elimination.
public static partial class MoiraiGrammar
{
    static TokenListParser<MoiraiTokenKind, Token<MoiraiTokenKind>> Kind(MoiraiTokenKind kind) =>
        Token.EqualTo(kind);

    static readonly TokenListParser<MoiraiTokenKind, Token<MoiraiTokenKind>[]> LineBreaksStar =
        Kind(MoiraiTokenKind.LineBreak).Many();

    static readonly TokenListParser<MoiraiTokenKind, Token<MoiraiTokenKind>[]> LineBreaksPlus =
        Kind(MoiraiTokenKind.LineBreak).AtLeastOnce();

    /// Combine two spans (both must be slices of the same source) into the span from the first's
    /// start through the second's end — used to compute every node's overall Span from its first
    /// and last consumed token/child span.
    static TextSpan Combine(TextSpan first, TextSpan last) =>
        new(first.Source, first.Position, last.Position.Absolute + last.Length - first.Position.Absolute);

    static TextSpan Combine(Token<MoiraiTokenKind> first, Token<MoiraiTokenKind> last) =>
        Combine(first.Span, last.Span);

    static Ident IdentOf(Token<MoiraiTokenKind> t) => new(t.Span, t.ToStringValue());

    /// Peek the next token's kind without consuming input (TokenList is positional/immutable —
    /// ConsumeToken() on `input` doesn't mutate it, so this is a pure lookahead).
    static MoiraiTokenKind? PeekKind(TokenList<MoiraiTokenKind> input)
    {
        var r = input.ConsumeToken();
        return r.HasValue ? r.Value.Kind : null;
    }

    static MoiraiTokenKind? PeekKindAt(TokenList<MoiraiTokenKind> input, int aheadTokens)
    {
        var cur = input;
        for (int i = 0; i < aheadTokens; i++)
        {
            var r = cur.ConsumeToken();
            if (!r.HasValue) return null;
            cur = r.Remainder;
        }

        var next = cur.ConsumeToken();
        return next.HasValue ? next.Value.Kind : null;
    }

    // ---- Public entry points -------------------------------------------------------------

    public static TokenListParserResult<MoiraiTokenKind, RNode> TryParseR(TokenList<MoiraiTokenKind> tokens) =>
        R(tokens);

    public static TokenListParserResult<MoiraiTokenKind, ExprNode> TryParseExpr(TokenList<MoiraiTokenKind> tokens) =>
        Expr(tokens);
}
