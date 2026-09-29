using System.Text;

namespace Moirai.Parser;

/// What an attribute's argument has to be. Each kind is checked the same way for every attribute, in
/// AstVisitor.BindAttribute, before the attribute's own handler runs.
public enum AttributeArgKind
{
    /// A whole-number literal, `3`.
    Number,
    /// One of a fixed set of names, written bare (`PerXYear`) or qualified by its enum (`Frequency.PerXYear`).
    Choice,
    /// A plain string literal, `'war'`.
    String,
    /// A string literal that may interpolate, `'{$other.name}'`.
    Text,
    /// The name of an entity type, `Person`.
    EntityType,
    /// An expression over the entity type another argument names.
    Predicate,
    /// The name of a property of the annotated type.
    Property,
}

/// What a <see cref="AttributeArgKind.Property"/> argument's property has to hold.
public enum PropertyKind
{
    /// A reference to the annotated type itself (a parent, a partner).
    SelfReference,
    /// A number (a year).
    Number,
    /// A bool (a life or death flag).
    Bool,
}

/// One parameter of an attribute. Only the last one may be <see cref="Optional"/> or <see cref="Repeated"/>.
public sealed record AttributeParam(string Name, AttributeArgKind Kind)
{
    /// May be left out.
    public bool Optional { get; init; }
    /// One or more, comma-separated.
    public bool Repeated { get; init; }
    /// For <see cref="AttributeArgKind.Choice"/>: the accepted names, and the enum that may qualify them.
    public string[]? Choices { get; init; }
    public string? ChoiceEnum { get; init; }
    /// For <see cref="AttributeArgKind.Number"/>: the smallest accepted value.
    public int? Min { get; init; }
    /// For <see cref="AttributeArgKind.Property"/>.
    public PropertyKind? PropertyKind { get; init; }
    /// For <see cref="AttributeArgKind.Predicate"/>: the index of the <see cref="AttributeArgKind.EntityType"/>
    /// argument whose entities it is evaluated over.
    public int? Over { get; init; }

    /// How the parameter is written in a signature.
    public string Syntax => Kind switch
    {
        AttributeArgKind.Choice => string.Join(" | ", Choices!),
        AttributeArgKind.String or AttributeArgKind.Text => $"'{Name}'",
        _ => Name,
    };

    /// What the parameter accepts, as a phrase: "a whole-number literal, at least 1".
    public string Describe(IReadOnlyList<AttributeParam> all) => Kind switch
    {
        AttributeArgKind.Number => Min is { } m ? $"a whole-number literal, at least {m}" : "a whole-number literal",
        AttributeArgKind.Choice => "one of " + string.Join(", ", Choices!.Select(c => $"`{c}`")),
        AttributeArgKind.String => "a string literal",
        AttributeArgKind.Text => "a string literal, which can interpolate `$other`",
        AttributeArgKind.EntityType => "an entity type",
        AttributeArgKind.Predicate =>
            $"a predicate over the `{all[Over!.Value].Name}` entities: `$self` is the annotated entity, `$other` the candidate, and a bare property name reads `$other`'s",
        AttributeArgKind.Property => PropertyKind switch
        {
            Parser.PropertyKind.SelfReference => "a property of the annotated type that refers to that same type",
            Parser.PropertyKind.Number => "a number property of the annotated type",
            _ => "a bool property of the annotated type",
        },
        _ => throw new ArgumentOutOfRangeException(),
    };
}

/// Shorthands for declaring <see cref="AttributeParam"/>s.
public static class Arg
{
    public static AttributeParam Number(string name, int? min = null) => new(name, AttributeArgKind.Number) { Min = min };

    public static AttributeParam Choice<T>(string name) where T : struct, Enum =>
        new(name, AttributeArgKind.Choice) { Choices = Enum.GetNames<T>(), ChoiceEnum = typeof(T).Name };

    public static AttributeParam String(string name) => new(name, AttributeArgKind.String);
    public static AttributeParam Text(string name) => new(name, AttributeArgKind.Text);
    public static AttributeParam EntityType(string name) => new(name, AttributeArgKind.EntityType);
    public static AttributeParam Predicate(string name, int over) => new(name, AttributeArgKind.Predicate) { Over = over };

    public static AttributeParam Property(string name, PropertyKind kind) =>
        new(name, AttributeArgKind.Property) { PropertyKind = kind };

    public static AttributeParam Optional(this AttributeParam p) => p with { Optional = true };
    public static AttributeParam Repeated(this AttributeParam p) => p with { Repeated = true };
}

/// One `@name(...)` attribute: where it may appear, the arguments it takes, and its documentation. Its
/// signature is generated from <see cref="Params"/>, so what the reference shows is exactly what the parser
/// accepts. <see cref="StoryParser.Attributes"/> is the registry the parser checks before lowering an
/// attribute, so an attribute that is not documented here is not accepted either.
public sealed record AttributeDescriptor(
    string Name,
    AttributeTarget Targets,
    AttributeParam[] Params,
    DocCategory Category,
    string Summary,
    string Example)
{
    public int MinArgs => Params.Count(p => !p.Optional);
    public int? MaxArgs => Params.Any(p => p.Repeated) ? null : Params.Length;

    /// The parameter an argument at <paramref name="index"/> is bound to: a repeated last parameter takes
    /// every argument from its position on.
    public AttributeParam? ParamAt(int index) =>
        index < Params.Length ? Params[index] : Params.Length > 0 && Params[^1].Repeated ? Params[^1] : null;

    /// `@frequency(x, PerXYear | EveryXYear, y)`, `@display(OtherType, 'Label', predicate[, 'item format'])`,
    /// `@tag('name', ...)`, `@start`.
    public string Signature
    {
        get
        {
            if (Params.Length == 0)
                return "@" + Name;
            var b = new StringBuilder("@").Append(Name).Append('(');
            for (int i = 0; i < Params.Length; i++)
            {
                var p = Params[i];
                if (p.Optional)
                    b.Append(i == 0 ? "[" : "[, ").Append(p.Syntax).Append(']');
                else
                    b.Append(i == 0 ? "" : ", ").Append(p.Syntax);
                if (p.Repeated)
                    b.Append(", ...");
            }

            return b.Append(')').ToString();
        }
    }

    public BuiltinDoc Doc => new(Category, [Signature], Summary, Example);
}
