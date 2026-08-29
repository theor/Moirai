using Superpower.Model;

namespace Moirai.Parser;

public readonly record struct TokenizerError(string Message, Position Position);

public sealed class MoiraiTokenizerResult
{
    /// Every token, including Space/Comment trivia — the ANTLR-HIDDEN-channel equivalent.
    public required TokenList<MoiraiTokenKind> FullTokens { get; init; }

    /// Space/Comment filtered out (LineBreak is kept — it's grammar-significant); this is what the
    /// Superpower grammar combinators (Phase 2) consume.
    public required TokenList<MoiraiTokenKind> ParseTokens { get; init; }

    /// ParseTokens[i] corresponds to FullTokens[ParseIndexToFullIndex[i]] — lets later LSP work
    /// (deferred; see the migration plan) recover leading/trailing comments for a parse-tree node
    /// without re-tokenizing.
    public required int[] ParseIndexToFullIndex { get; init; }

    public required IReadOnlyList<TokenizerError> Errors { get; init; }
}

/// Hand-rolled scanner for the Moirai DSL, replacing moirai_lexer.g4. Not derived from Superpower's
/// Tokenizer&lt;TKind&gt; base (which assumes a flat, mode-free scan) because string interpolation
/// requires a genuine mode stack: `'text {expr} more text'` — quote pushes InString; inside InString,
/// `{` pushes back to Default to lex the interpolated expression; `}` in Default pops whatever mode
/// is on top of the stack (this is NOT interpolation-specific — every brace pair does this: type
/// bodies, function bodies, match blocks, create{...} initializers); the closing quote pops out of
/// InString entirely. Getting this invariant exactly right (not as an "interpolation special case")
/// is what makes arbitrary-depth nested interpolation work, matching moirai_lexer.g4's mode rules.
public static class MoiraiTokenizer
{
    enum LexMode { Default, InString }

    static readonly Dictionary<string, MoiraiTokenKind> Keywords = new()
    {
        ["null"] = MoiraiTokenKind.Null,
        ["event"] = MoiraiTokenKind.Event,
        ["entity"] = MoiraiTokenKind.Entity,
        ["singleton"] = MoiraiTokenKind.Singleton,
        ["trigger"] = MoiraiTokenKind.Trigger,
        ["prop"] = MoiraiTokenKind.Prop,
        ["function"] = MoiraiTokenKind.Function,
        ["enum"] = MoiraiTokenKind.Enum,
        ["table"] = MoiraiTokenKind.Table,
        ["when"] = MoiraiTokenKind.When,
        ["when_created"] = MoiraiTokenKind.WhenCreated,
        ["set"] = MoiraiTokenKind.Set,
        ["var"] = MoiraiTokenKind.Var,
        ["match"] = MoiraiTokenKind.Match,
        ["random_weighted"] = MoiraiTokenKind.MatchWeight,
        ["if"] = MoiraiTokenKind.If,
        ["else"] = MoiraiTokenKind.Else,
        ["true"] = MoiraiTokenKind.True,
        ["false"] = MoiraiTokenKind.False,
        ["and"] = MoiraiTokenKind.And,
        ["or"] = MoiraiTokenKind.Or,
    };

    static bool IsUpper(char c) => c is >= 'A' and <= 'Z';
    static bool IsLower(char c) => c is >= 'a' and <= 'z';
    static bool IsAlpha(char c) => IsUpper(c) || IsLower(c);
    static bool IsDigit(char c) => c is >= '0' and <= '9';

    public static MoiraiTokenizerResult Tokenize(string source)
    {
        var state = new State(source);
        var full = new List<Token<MoiraiTokenKind>>();
        while (true)
        {
            var token = state.ScanNext();
            if (token == null)
                break;
            full.Add(token.Value);
        }

        var parse = new List<Token<MoiraiTokenKind>>();
        var parseIndexToFull = new List<int>();
        for (int i = 0; i < full.Count; i++)
        {
            if (full[i].Kind is MoiraiTokenKind.Space or MoiraiTokenKind.Comment)
                continue;
            parse.Add(full[i]);
            parseIndexToFull.Add(i);
        }

        return new MoiraiTokenizerResult
        {
            FullTokens = new TokenList<MoiraiTokenKind>(full.ToArray()),
            ParseTokens = new TokenList<MoiraiTokenKind>(parse.ToArray()),
            ParseIndexToFullIndex = parseIndexToFull.ToArray(),
            Errors = state.Errors,
        };
    }

    sealed class State(string source)
    {
        readonly string _source = source;
        int _index;
        Position _pos = new(0, 1, 1);
        readonly Stack<LexMode> _modes = new(new[] { LexMode.Default });
        public readonly List<TokenizerError> Errors = new();

        char Current => _index < _source.Length ? _source[_index] : '\0';
        char Peek(int offset) => _index + offset < _source.Length ? _source[_index + offset] : '\0';
        bool AtEnd => _index >= _source.Length;

        void Advance()
        {
            _pos = _pos.Advance(_source[_index]);
            _index++;
        }

        void RecordError(string message, Position at) => Errors.Add(new TokenizerError(message, at));

        /// Pop the mode stack, but never below the floor (the implicit root Default mode) — an
        /// unbalanced closing brace/quote in malformed/transient input must never crash the
        /// tokenizer, only get flagged.
        void PopModeGuarded(string context, Position at)
        {
            if (_modes.Count > 1)
                _modes.Pop();
            else
                RecordError($"Unbalanced '{context}' — nothing to close", at);
        }

        public Token<MoiraiTokenKind>? ScanNext() =>
            _modes.Peek() == LexMode.Default ? ScanDefault() : ScanInString();

        Token<MoiraiTokenKind> Emit(MoiraiTokenKind kind, Position start) =>
            new(kind, new TextSpan(_source, start, _index - start.Absolute));

        Token<MoiraiTokenKind>? ScanInString()
        {
            if (AtEnd)
                return null;

            var start = _pos;
            char c = Current;

            if (c == '\'')
            {
                Advance();
                PopModeGuarded("'", start);
                return Emit(MoiraiTokenKind.Quote, start);
            }

            if (c == '{')
            {
                Advance();
                _modes.Push(LexMode.Default);
                return Emit(MoiraiTokenKind.ExprOpen, start);
            }

            // TEXT: (QUOTED_QUOTE | ~['{])+ — greedy run up to (not including) an unescaped quote
            // or brace. \' is a 2-char escape consumed as ordinary text; any other backslash is
            // just an ordinary character (no other escape sequence exists in this grammar).
            while (!AtEnd)
            {
                c = Current;
                if (c == '\\' && Peek(1) == '\'')
                {
                    Advance();
                    Advance();
                    continue;
                }

                if (c == '\'' || c == '{')
                    break;

                Advance();
            }

            return Emit(MoiraiTokenKind.Text, start);
        }

        Token<MoiraiTokenKind>? ScanDefault()
        {
            if (AtEnd)
                return null;

            var start = _pos;
            char c = Current;

            switch (c)
            {
                case ' ' or '\t':
                    while (Current is ' ' or '\t') Advance();
                    return Emit(MoiraiTokenKind.Space, start);

                case '\r':
                    Advance();
                    if (Current == '\n') Advance();
                    return Emit(MoiraiTokenKind.LineBreak, start);

                case '\n':
                    Advance();
                    return Emit(MoiraiTokenKind.LineBreak, start);

                case '/':
                    if (Peek(1) == '/')
                    {
                        Advance();
                        Advance();
                        while (!AtEnd && Current is not ('\r' or '\n')) Advance();
                        return Emit(MoiraiTokenKind.Comment, start);
                    }

                    Advance();
                    return Emit(MoiraiTokenKind.Div, start);

                case '\'':
                    Advance();
                    _modes.Push(LexMode.InString);
                    return Emit(MoiraiTokenKind.Quote, start);

                case '{':
                    Advance();
                    _modes.Push(LexMode.Default);
                    return Emit(MoiraiTokenKind.ScopeOpen, start);

                case '}':
                    Advance();
                    PopModeGuarded("}", start);
                    return Emit(MoiraiTokenKind.ScopeClose, start);

                case '(':
                    Advance();
                    return Emit(MoiraiTokenKind.ParenOpen, start);

                case ')':
                    Advance();
                    return Emit(MoiraiTokenKind.ParenClose, start);

                case '[':
                    Advance();
                    return Emit(MoiraiTokenKind.LBrack, start);

                case ']':
                    Advance();
                    return Emit(MoiraiTokenKind.RBrack, start);

                case ',':
                    Advance();
                    return Emit(MoiraiTokenKind.Comma, start);

                case '.':
                    Advance();
                    return Emit(MoiraiTokenKind.Dot, start);

                case '+':
                    Advance();
                    return Emit(MoiraiTokenKind.Add, start);

                case '*':
                    Advance();
                    return Emit(MoiraiTokenKind.Mul, start);

                case '@':
                    Advance();
                    return Emit(MoiraiTokenKind.At, start);

                case ':':
                    Advance();
                    if (Current == '=')
                    {
                        Advance();
                        return Emit(MoiraiTokenKind.ColonEq, start);
                    }

                    return Emit(MoiraiTokenKind.Colon, start);

                case '=':
                    Advance();
                    if (Current == '>')
                    {
                        Advance();
                        return Emit(MoiraiTokenKind.Arrow, start);
                    }

                    return Emit(MoiraiTokenKind.Eq, start);

                case '!':
                    if (Peek(1) == '=')
                    {
                        Advance();
                        Advance();
                        return Emit(MoiraiTokenKind.Neq, start);
                    }

                    Advance();
                    RecordError("Unrecognized character '!' (no standalone '!' token)", start);
                    return Emit(MoiraiTokenKind.Unknown, start);

                case '?':
                    if (Peek(1) == '?')
                    {
                        Advance();
                        Advance();
                        return Emit(MoiraiTokenKind.Qq, start);
                    }

                    Advance();
                    RecordError("Unrecognized character '?' (no standalone '?' token)", start);
                    return Emit(MoiraiTokenKind.Unknown, start);

                case '>':
                    Advance();
                    if (Current == '=')
                    {
                        Advance();
                        return Emit(MoiraiTokenKind.Ge, start);
                    }

                    return Emit(MoiraiTokenKind.Gt, start);

                case '<':
                    Advance();
                    if (Current == '=')
                    {
                        Advance();
                        return Emit(MoiraiTokenKind.Le, start);
                    }

                    return Emit(MoiraiTokenKind.Lt, start);

                case '%':
                    // PROP_ID: '%' [a-z][a-z_]*  vs MOD: '%' — PROP_ID wins by maximal munch only
                    // when a lowercase letter follows; otherwise this is the MOD operator.
                    Advance();
                    if (IsLower(Current))
                    {
                        Advance();
                        while (IsLower(Current) || Current == '_') Advance();
                        return Emit(MoiraiTokenKind.PropId, start);
                    }

                    return Emit(MoiraiTokenKind.Mod, start);

                case '#':
                    // SINGLETON_ID: '#' ALPHA_UPPER (ALPHA|'_')* — mandatory uppercase letter
                    // immediately after '#'; no fallback rule for a lone '#'.
                    Advance();
                    if (IsUpper(Current))
                    {
                        Advance();
                        while (IsAlpha(Current) || Current == '_') Advance();
                        return Emit(MoiraiTokenKind.SingletonId, start);
                    }

                    RecordError("Unrecognized '#' — expected an uppercase letter (singleton reference)",
                        start);
                    return Emit(MoiraiTokenKind.Unknown, start);

                case '$':
                    // VAR_ID: '$' (ALPHA|DIGIT) (ALPHA|DIGIT|'_')* — mandatory alnum immediately
                    // after '$' (NOT '_' — "$_foo" has no valid VAR_ID match in the grammar).
                    Advance();
                    if (IsAlpha(Current) || IsDigit(Current))
                    {
                        Advance();
                        while (IsAlpha(Current) || IsDigit(Current) || Current == '_') Advance();
                        return Emit(MoiraiTokenKind.VarId, start);
                    }

                    RecordError("Unrecognized '$' — expected a letter or digit (variable reference)",
                        start);
                    return Emit(MoiraiTokenKind.Unknown, start);

                default:
                    if (c == '-' || IsDigit(c))
                    {
                        if (c == '-' && !IsDigit(Peek(1)))
                        {
                            Advance();
                            return Emit(MoiraiTokenKind.Sub, start);
                        }

                        if (c == '-') Advance(); // consume the sign; it's part of the literal
                        while (IsDigit(Current)) Advance();

                        if (Current == '.' && IsDigit(Peek(1)))
                        {
                            Advance();
                            while (IsDigit(Current)) Advance();
                            return Emit(MoiraiTokenKind.NumberFloat, start);
                        }

                        if (Current == '%')
                        {
                            Advance();
                            return Emit(MoiraiTokenKind.Percent, start);
                        }

                        return Emit(MoiraiTokenKind.Number, start);
                    }

                    if (IsUpper(c))
                    {
                        // TYPE_ID: ALPHA_UPPER (ALPHA|'_')* — no digits in continuation (unlike ID).
                        Advance();
                        while (IsAlpha(Current) || Current == '_') Advance();
                        return Emit(MoiraiTokenKind.TypeId, start);
                    }

                    if (IsLower(c) || c == '_')
                    {
                        Advance();
                        while (IsAlpha(Current) || Current == '_' || IsDigit(Current)) Advance();
                        var text = _source.Substring(start.Absolute, _index - start.Absolute);
                        var kind = Keywords.GetValueOrDefault(text, MoiraiTokenKind.Id);
                        return Emit(kind, start);
                    }

                    Advance();
                    RecordError($"Unrecognized character '{c}'", start);
                    return Emit(MoiraiTokenKind.Unknown, start);
            }
        }
    }
}
