using Superpower.Model;

namespace Moirai.Parser.Ast;

/// A name token (ID/TYPE_ID/VAR_ID/SINGLETON_ID/etc.) paired with its source span. Reused across
/// every AST node that previously captured a bare ANTLR ITerminalNode just to read its text and
/// build a FileRange from it (property/type/function/variable names, keywords used as error-report
/// anchors, ...).
public readonly record struct Ident(TextSpan Span, string Text)
{
    public override string ToString() => Text;
}

public enum NumberKind { Int, Float, Percent }

public sealed record RNode(DefNode[] Defs, TextSpan Span);

public sealed record DefNode(
    AttributeNode[] Attributes,
    EventNode? Event,
    TriggerNode? Trigger,
    EnumDefinitionNode? EnumDefinition,
    TypeDefinitionNode? TypeDefinition,
    FunctionDefinitionNode? FunctionDefinition,
    TableDefinitionNode? TableDefinition,
    TextSpan Span);

public sealed record AttributeNode(Ident Name, ExprNode[] Args, TextSpan Span);

public sealed record ParamNode(Ident VarId, TypeNode Type, TextSpan Span);

public sealed record EventNode(Ident Name, ParamNode[] Params, ScopeNode Scope, TextSpan Span);

public sealed record TriggerNode(Ident Name, ScopeNode Scope, TextSpan Span);

/// `keyword` is the WHEN/WHEN_CREATED token itself — AstVisitor uses its position as the anchor for
/// declaring $old/$new (previously `whenContext.WHEN().Symbol`).
public sealed record WhenNode(Ident Keyword, Ident TypeId, ExprNode[] Exprs, TextSpan Span);

public sealed record WhenCreatedNode(Ident Keyword, Ident TypeId, ExprNode[] Exprs, TextSpan Span);

public sealed record ScopeNode(WhenNode? When, WhenCreatedNode? WhenCreated, EffectNode[] Effects, TextSpan Span);

public sealed record EffectNode(ExprNode? Expr, VarNode? Var, SetNode? Set, InitNode? Init, TextSpan Span);

public sealed record IfNode(ExprNode Cond, ScopeNode Then, ScopeNode? Else, TextSpan Span);

public sealed record MatchNode(bool IsWeight, ExprNode[] Exprs, MatchCaseNode[] Cases, TextSpan Span);

public sealed record MatchCaseNode(ValueNode[] Values, EffectNode? Effect, ScopeNode? Scope, TextSpan Span);

public sealed record SetNode(PathNode Path, ExprNode Expr, TextSpan Span);

public sealed record InitNode(Ident PropertyId, ExprNode Expr, TextSpan Span);

public sealed record VarNode(Ident VarId, ExprNode Expr, TextSpan Span);

/// The parenthesized call form: `fun_id (type VAR_ID COLON)? PAREN_OPEN (expr (COMMA expr)*)? PAREN_CLOSE scope?`.
public sealed record CallNode(
    Ident FunId,
    TypeNode? DeclType,
    Ident? VarId,
    ExprNode[] Args,
    ScopeNode? Scope,
    TextSpan Span);

/// The bare/paren-less call form: `fun_id ((type VAR_ID (COLON value)?) | value) scope?`.
public sealed record RawCallNode(
    Ident FunId,
    TypeNode? DeclType,
    Ident? VarId,
    ValueNode? Value,
    ScopeNode? Scope,
    TextSpan Span);

public sealed record EnumValueNode(Ident EnumType, Ident Member, TextSpan Span);

public sealed record NumberNode(NumberKind Kind, string Text, TextSpan Span);

public abstract record StringPartNode(TextSpan Span);
public sealed record StringTextPart(string Text, TextSpan Span) : StringPartNode(Span);
public sealed record StringExprPart(ExprNode Expr, TextSpan Span) : StringPartNode(Span);

public sealed record StringNode(StringPartNode[] Parts, TextSpan Span);

public sealed record DotPropertyNode(Ident? Property, CallNode? Call, TextSpan Span);

/// `path: (var_id_read | property_id) dot_property*` — the root is exactly one of SingletonId,
/// VarId, or a bare PropertyId (the last case is also how the "implicit current-scope entity"
/// shorthand seen in space.sg — `set year = 0` instead of `set $e.year = 0` — surfaces: no
/// var_id_read at all, just a lone property_id root).
public sealed record PathNode(Ident? SingletonId, Ident? VarId, Ident? PropertyId, DotPropertyNode[] DotProperties, TextSpan Span);

public sealed record TypeNode(Ident Name, bool IsCollection, TextSpan Span);

public sealed record ValueNode(
    RawCallNode? RawCall,
    CallNode? Call,
    StringNode? StringLit,
    EnumValueNode? EnumValue,
    Ident? TypeId,
    PathNode? Path,
    bool? BoolValue,
    NumberNode? Number,
    bool IsNull,
    TextSpan Span);

/// `expr: if | match | value | left op right | PAREN_OPEN expr PAREN_CLOSE`. Exactly one of
/// If/Match/Value/Paren is set for a leaf/prefix expression; Op/Left/Right are set for a binary
/// operator node (built by the precedence-climbing parser, not directly by any single grammar
/// alternative — mirrors how ANTLR's left-recursion elimination produces the same shape).
public sealed record ExprNode(
    IfNode? If,
    MatchNode? Match,
    ValueNode? Value,
    ExprNode? Paren,
    string? Op,
    ExprNode? Left,
    ExprNode? Right,
    TextSpan Span);

public sealed record PropDefinitionNode(Ident PropertyId, TypeNode Type, TextSpan Span);

/// TypeName is null only when the grammar's mandatory-TYPE_ID position was matched by a lowercase
/// ID instead (recovery fallback preserving StoryParser.ErrorCode.TypenameMustStartWithUpperCase);
/// IsLowercaseName distinguishes that recovered case from "no name token found at all".
public sealed record TypeDefinitionNode(
    bool IsSingleton,
    Ident? TypeName,
    bool IsLowercaseName,
    PropDefinitionNode[] PropDefinitions,
    FunctionDefinitionNode[] FunctionDefinitions,
    TextSpan Span);

public sealed record EnumDefinitionNode(Ident Name, Ident[] Members, TextSpan Span);

public sealed record TableEntryNode(int? Weight, ValueNode Value, TextSpan Span);

public sealed record TableDefinitionNode(Ident Name, TableEntryNode[] Entries, TextSpan Span);

public sealed record FunctionDefinitionNode(
    Ident Name,
    ParamNode[] Params,
    TypeNode? ReturnType,
    ScopeNode Scope,
    TextSpan Span);
