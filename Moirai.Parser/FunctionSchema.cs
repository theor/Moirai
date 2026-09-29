using System.Text;

namespace Moirai.Parser;

/// How a built-in function's argument is written. Every kind but <see cref="Value"/> is a syntactic form
/// the binder recognises; a Value is any expression, checked against its <see cref="ValueConstraint"/>.
public enum FnArgKind
{
    /// Any expression, of the type its constraint allows.
    Value,
    /// A path ending in a collection property, `$e.friends`.
    Collection,
    /// The name of an enum, `Job`.
    EnumType,
    /// The name of a table, `Name`.
    Table,
    /// A string literal, which may interpolate: `'{$p.name} arrives'`.
    Text,
    /// A whole-number literal, optionally within bounds.
    IntLiteral,
}

/// What type a <see cref="FnArgKind.Value"/> argument must have.
public enum ValueConstraint
{
    Any,
    /// A number, a float or a percentage.
    Number,
    Bool,
    /// A bool, or an entity, which is true when there is one: what `if` accepts, so `assert(pick T $v: (...))`
    /// reads as "something matches".
    Condition,
    /// A reference to an entity.
    Entity,
    String,
    /// What the collection argument at <see cref="FnParam.Of"/> holds.
    ElementOf,
}

/// One parameter of a built-in function's form. Only a form's last parameter may be optional or repeated.
public sealed record FnParam(string Name, FnArgKind Kind)
{
    public ValueConstraint Constraint { get; init; }
    /// For <see cref="ValueConstraint.ElementOf"/>: the index of the collection parameter.
    public int Of { get; init; }
    /// For <see cref="FnArgKind.IntLiteral"/>.
    public int? Min { get; init; }
    public int? Max { get; init; }
    public bool Optional { get; init; }
    /// One or more, comma-separated.
    public bool Repeated { get; init; }

    /// The parameter's type as a signature writes it: `number`, `collection`, `0..6`.
    public string TypeText(IReadOnlyList<FnParam> all) => Kind switch
    {
        FnArgKind.Value => Constraint switch
        {
            ValueConstraint.Number => "number",
            ValueConstraint.Bool => "bool",
            ValueConstraint.Condition => "condition",
            ValueConstraint.Entity => "entity",
            ValueConstraint.String => "string",
            ValueConstraint.ElementOf => $"element of {all[Of].Name}",
            _ => "any",
        },
        FnArgKind.Collection => "collection",
        FnArgKind.EnumType => "enum",
        FnArgKind.Table => "table",
        FnArgKind.Text => "text",
        FnArgKind.IntLiteral => Min is { } lo && Max is { } hi ? $"{lo}..{hi}" : "number literal",
        _ => throw new ArgumentOutOfRangeException(),
    };

    /// `x: number`, and for a text parameter `'text'`, since the quotes are the whole point.
    public string Syntax(IReadOnlyList<FnParam> all) =>
        Kind == FnArgKind.Text ? $"'{Name}'" : $"{Name}: {TypeText(all)}";
}

/// What a form returns. <see cref="EnumOfArg"/> and <see cref="TableEntryOfArg"/> take their type from
/// the enum or table named by argument 0; <see cref="Variable"/> is the entity the form binds; a handler
/// may refine <see cref="Number"/> (an aggregate's type follows its value's).
public enum FnReturn
{
    Nothing,
    Number,
    Bool,
    String,
    EnumOfArg,
    TableEntryOfArg,
    Variable,
}

/// When a block's statements run.
public enum BlockRuns
{
    /// When the draw hits (`chance(p) { ... }`).
    OnHit,
    /// When nothing matched (`pick ... else { ... }`).
    OnMiss,
    /// Once per match (`each ... { ... }`).
    PerMatch,
    /// Right after the entity is made, on it (`create ... { prop := value }`).
    OnCreate,
    /// In a later year, as a rule of its own (`schedule(...) { ... }`).
    Later,
    /// A given number of times (`repeat(n) { ... }`).
    Times,
}

/// What a block can see.
public enum BlockSees
{
    /// The enclosing rule's locals.
    Enclosing,
    /// The enclosing locals and the form's variable.
    EnclosingAndVariable,
    /// The enclosing locals but not the form's variable, which does not exist when the block runs.
    EnclosingNotVariable,
    /// `$self`, typed after argument 0, and nothing else from the rule: it runs later, when those locals
    /// are gone.
    OnlySelf,
}

/// A block a form carries: `{ ... }`, or `else { ... }` when <see cref="Keyword"/> is set. What it runs,
/// when, and what it sees are declared here, and the binder sets up its scope from them.
public sealed record BlockSpec(BlockRuns Runs, BlockSees Sees, string? Keyword = null)
{
    /// `prop := value` lines rather than statements.
    public bool Initializer => Runs == BlockRuns.OnCreate;

    public string Syntax => (Keyword == null ? "" : Keyword + " ") + (Initializer ? "{ prop := value ... }" : "{ ... }");

    public string Describe() => Runs switch
    {
        BlockRuns.OnHit => "runs when the draw hits",
        BlockRuns.OnMiss => "runs when nothing matches, then the rule stops successfully",
        BlockRuns.PerMatch => "runs once per match",
        BlockRuns.OnCreate => "sets properties on the new entity",
        BlockRuns.Times => "runs n times; a stop ends that turn only",
        _ => "runs in a later year, as a rule of its own",
    } + Sees switch
    {
        BlockSees.EnclosingAndVariable => "; sees the rule's locals and $v",
        BlockSees.EnclosingNotVariable => "; sees the rule's locals but not $v",
        BlockSees.OnlySelf => "; sees only $self",
        _ => "; sees the rule's locals",
    };
}

/// One way a built-in can be written. A <see cref="CallForm"/> is a plain `name(arg, ...)`; a
/// <see cref="BindingForm"/> introduces a variable (`pick T $v: (...)`). Either may carry blocks, and a
/// call matches a form only if it has exactly the blocks the form declares.
public abstract record FunctionForm
{
    public BlockSpec[] Blocks { get; init; } = [];
    public abstract FnReturn Returns { get; }
    public abstract string Signature(string name);

    protected string WithBlocks(string head) =>
        Blocks.Length == 0 ? head : head + " " + string.Join(" ", Blocks.Select(b => b.Syntax));
}

/// `name(arg, ...)`: parameters in order, and what the call returns.
public sealed record CallForm(FnParam[] Params, FnReturn ReturnsValue) : FunctionForm
{
    public override FnReturn Returns => ReturnsValue;
    public int MinArgs => Params.Count(p => !p.Optional);
    public int? MaxArgs => Params.Any(p => p.Repeated) ? null : Params.Length;

    public FnParam? ParamAt(int index) =>
        index < Params.Length ? Params[index] : Params.Length > 0 && Params[^1].Repeated ? Params[^1] : null;

    public string ReturnText => Returns switch
    {
        FnReturn.Number => "number",
        FnReturn.Bool => "bool",
        FnReturn.String => "string",
        FnReturn.EnumOfArg => Params[0].Name,
        FnReturn.TableEntryOfArg => $"entry of {Params[0].Name}",
        _ => "",
    };

    /// `floor(x: number): number`, `record('text'[, weight: number])`, `chance(p: number) { ... }`.
    public override string Signature(string name)
    {
        var b = new StringBuilder(name).Append('(');
        for (int i = 0; i < Params.Length; i++)
        {
            var p = Params[i];
            var text = p.Syntax(Params);
            if (p.Optional)
                b.Append(i == 0 ? "[" : "[, ").Append(text).Append(']');
            else
                b.Append(i == 0 ? "" : ", ").Append(text);
            if (p.Repeated)
                b.Append(", ...");
        }

        b.Append(')');
        if (Returns != FnReturn.Nothing)
            b.Append(": ").Append(ReturnText);
        return WithBlocks(b.ToString());
    }
}

/// What follows a binding form's variable.
public enum BindingHead
{
    /// `T $v`
    Variable,
    /// `T $v: 'name'`
    Name,
    /// `T $v[: (predicate...)]`: clauses joined by `and`; none means every T.
    Predicate,
    /// `T $v: ([predicate..., ]value)`: the last argument is the value, the rest the predicate.
    PredicateAndValue,
}

/// How long a binding form's variable lives.
public enum VariableLives
{
    /// For the rest of the rule (`pick`, `create`).
    Rest,
    /// Inside the form's block (`each`).
    Block,
    /// Inside the form's parentheses (`count`, `sum`).
    Call,
}

/// `name T $v...`: a form that introduces a variable, with its head, its variable's lifetime and its blocks.
public sealed record BindingForm(BindingHead Head, VariableLives Lives, FnReturn ReturnsValue = FnReturn.Nothing)
    : FunctionForm
{
    public override FnReturn Returns => ReturnsValue;

    /// The value argument of a <see cref="BindingHead.PredicateAndValue"/> head.
    public FnParam? Value { get; init; }

    public override string Signature(string name)
    {
        var head = Head switch
        {
            BindingHead.Variable => "T $v",
            BindingHead.Name => "T $v: 'name'",
            BindingHead.Predicate => "T $v[: (predicate...)]",
            _ => $"T $v: ([predicate..., ]{Value?.Syntax([Value]) ?? "value"})",
        };
        var ret = Returns switch
        {
            FnReturn.Number => ": number",
            FnReturn.Bool => ": bool",
            _ => "",
        };
        return WithBlocks($"{name} {head}{ret}");
    }
}

/// Shorthands for declaring <see cref="FnParam"/>s.
public static class P
{
    public static FnParam Any(string name) => new(name, FnArgKind.Value);
    public static FnParam Number(string name) => new(name, FnArgKind.Value) { Constraint = ValueConstraint.Number };
    public static FnParam Bool(string name) => new(name, FnArgKind.Value) { Constraint = ValueConstraint.Bool };
    public static FnParam Condition(string name) => new(name, FnArgKind.Value) { Constraint = ValueConstraint.Condition };
    public static FnParam Entity(string name) => new(name, FnArgKind.Value) { Constraint = ValueConstraint.Entity };
    public static FnParam String(string name) => new(name, FnArgKind.Value) { Constraint = ValueConstraint.String };

    public static FnParam ElementOf(string name, int collection) =>
        new(name, FnArgKind.Value) { Constraint = ValueConstraint.ElementOf, Of = collection };

    public static FnParam Collection(string name) => new(name, FnArgKind.Collection);
    public static FnParam EnumType(string name) => new(name, FnArgKind.EnumType);
    public static FnParam Table(string name) => new(name, FnArgKind.Table);
    public static FnParam Text(string name) => new(name, FnArgKind.Text);

    public static FnParam IntLiteral(string name, int? min = null, int? max = null) =>
        new(name, FnArgKind.IntLiteral) { Min = min, Max = max };

    public static FnParam Optional(this FnParam p) => p with { Optional = true };
    public static FnParam Repeated(this FnParam p) => p with { Repeated = true };

    public static CallForm Call(FnReturn returns, params FnParam[] ps) => new(ps, returns);

    public static T With<T>(this T form, params BlockSpec[] blocks) where T : FunctionForm => form with { Blocks = blocks };
}

/// The documentation of a built-in whose signatures are generated from its forms.
public sealed record FunctionDoc(DocCategory Category, string Summary, string Example);
