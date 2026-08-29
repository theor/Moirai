namespace Moirai.Parser;

/// Mirrors moirai_lexer.g4's token set 1:1 (see the ANTLR->Superpower migration plan,
/// C:\Users\theor\.claude\plans\stateful-dancing-stroustrup.md, Phase 1). Kept as a flat enum with
/// no code generation — <see cref="MoiraiTokenizer"/> is a hand-rolled scanner, not built on
/// Superpower's Tokenizer&lt;TKind&gt; base, because the grammar's lexer-mode-stack behavior (string
/// interpolation) needs an explicit mode stack rather than flat combinators.
public enum MoiraiTokenKind
{
    // Trivia (present in the full token list, excluded from the parser-facing list).
    Space,
    Comment,

    // Grammar-significant even though whitespace-adjacent: LINE_BREAK is a real statement
    // separator in MoiraiParser.g4, not skipped trivia.
    LineBreak,

    Quote,
    Null,
    ColonEq,
    Colon,
    ScopeOpen,
    ScopeClose,
    ExprOpen,
    ParenOpen,
    ParenClose,
    LBrack,
    RBrack,
    Event,
    Entity,
    Singleton,
    Trigger,
    Prop,
    Function,
    Enum,
    Table,
    When,
    WhenCreated,
    Set,
    Var,
    Match,
    MatchWeight,
    Comma,
    Arrow,
    If,
    Else,
    True,
    False,
    Dot,
    Neq,
    Eq,
    Qq,
    Add,
    Sub,
    Mul,
    Div,
    Mod,
    Ge,
    Le,
    Gt,
    Lt,
    And,
    Or,
    SingletonId,
    VarId,
    PropId,
    At,
    TypeId,
    Id,
    Percent,
    NumberFloat,
    Number,

    // IN_STRING mode only.
    Text,

    /// An unrecognized character (or a malformed sigil like a lone '$'/'#'/'!'/'?' with no valid
    /// continuation). Never thrown — the tokenizer always emits a token and keeps scanning; see
    /// MoiraiTokenizerResult.Errors for the diagnostic.
    Unknown,
}
