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
/// the enum or table named by argument 0.
public enum FnReturn
{
    Nothing,
    Number,
    Bool,
    String,
    EnumOfArg,
    TableEntryOfArg,
}

/// One way a built-in can be written. A <see cref="CallForm"/> is a plain `name(arg, ...)`; a
/// <see cref="BindingForm"/> introduces a variable (`pick T $v: (...)`).
public abstract record FunctionForm
{
    public abstract string Signature(string name);
}

/// `name(arg, ...)`: parameters in order, and what the call returns.
public sealed record CallForm(FnParam[] Params, FnReturn Returns) : FunctionForm
{
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

    /// `floor(x: number): number`, `record('text'[, weight: number])`, `debug(value: any, ...)`.
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
        return Returns == FnReturn.Nothing ? b.ToString() : b.Append(": ").Append(ReturnText).ToString();
    }
}

/// What follows a binding form's variable.
public enum BindingHead
{
    /// `T $v`
    Variable,
    /// `T $v: 'name'`
    Name,
    /// `T $v: (predicate...)`
    Predicate,
    /// `T $v: (predicate..., value)`
    PredicateAndValue,
}

/// `name T $v...`: a form that introduces a variable, optionally followed by a block. These are described,
/// not enforced: their parsing is scoping-sensitive (a pick's `else` cannot see `$v`, an each's body can)
/// and stays hand-written, so the reference's examples, which are parsed, are what keeps the two honest.
public sealed record BindingForm(BindingHead Head, string? Block, FnReturn Returns = FnReturn.Nothing) : FunctionForm
{
    public override string Signature(string name)
    {
        var head = Head switch
        {
            BindingHead.Variable => "T $v",
            BindingHead.Name => "T $v: 'name'",
            BindingHead.Predicate => "T $v: (predicate...)",
            _ => "T $v: (predicate..., value)",
        };
        return Block == null ? $"{name} {head}" : $"{name} {head} {Block}";
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
}

/// The documentation of a built-in whose signatures are generated from its forms.
public sealed record FunctionDoc(DocCategory Category, string Summary, string Example);
