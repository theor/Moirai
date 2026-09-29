using System.Text;

namespace Moirai.Parser;

/// Where an entry sits in the reference. The order here is the order of the sections.
public enum DocCategory
{
    Queries,
    Entities,
    Rules,
    Randomness,
    Records,
    Kinship,
    Collections,
    Math,
    Testing,
    EventAttributes,
    TypeAttributes,
}

/// The documentation of one built-in function or attribute: what the language server shows on hover and
/// completion, and what the language reference is generated from.
///
/// <see cref="Example"/> is real code, and `LanguageReferenceTests` parses every one of them against a
/// small fixture story and fails on any error, so an example cannot rot. A function's example is the
/// body of an event; an attribute's is one or more top-level definitions.
public sealed record BuiltinDoc(DocCategory Category, string[] Signatures, string Summary, string? Example = null)
{
    /// Hover and completion text: the signatures as code, then the summary.
    public string ToMarkdown()
    {
        var b = new StringBuilder();
        b.Append("```moirai\n").AppendJoin('\n', Signatures).Append("\n```\n\n").Append(Summary);
        return b.ToString();
    }
}

/// What an attribute can annotate.
[Flags]
public enum AttributeTarget
{
    Event = 1,
    Trigger = 2,
    Type = 4,
}

/// The language reference as data: every built-in function and attribute with its documentation.
/// `docs/language-reference.json` is this, serialized; the Markdown reference and the Story page's hover
/// and completion are generated from that file, and `LanguageReferenceTests` keeps it in step with the code.
public static class LanguageReference
{
    /// Each section's heading, in section order.
    public static readonly IReadOnlyList<(DocCategory Category, string Title)> Sections =
    [
        (DocCategory.Queries, "Queries"),
        (DocCategory.Entities, "Entities"),
        (DocCategory.Rules, "Rules and scheduling"),
        (DocCategory.Randomness, "Randomness"),
        (DocCategory.Records, "Records"),
        (DocCategory.Kinship, "Kinship"),
        (DocCategory.Collections, "Collections"),
        (DocCategory.Math, "Math and logic"),
        (DocCategory.Testing, "Testing and debugging"),
        (DocCategory.EventAttributes, "Event and trigger attributes"),
        (DocCategory.TypeAttributes, "Type attributes"),
    ];

    public sealed record Entry(string Kind, string Name, DocCategory Category, string[] Signatures,
        string Summary, string? Example, string[]? Targets = null, Parameter[]? Parameters = null,
        Form[]? Forms = null, bool? Checked = null);

    /// One way to write a built-in, as data: a plain call's parameters and return type, or a binding form's
    /// head (`predicate`, `predicateAndValue`, ...) and block. `Signature` is the same form as text.
    public sealed record Form(string Kind, string Signature, FormParameter[]? Parameters = null, string? Returns = null,
        string? Head = null, string? Block = null);

    /// A plain call's parameter: `Kind` is the FnArgKind, `Type` how a signature writes it (`number`, `0..6`).
    public sealed record FormParameter(string Name, string Kind, string Type, bool? Optional, bool? Repeated);

    /// An attribute's parameter as the reference shows it. `Kind` is the AttributeArgKind, `Accepts` the
    /// phrase a reader sees ("a whole-number literal, at least 1").
    public sealed record Parameter(string Name, string Kind, string Accepts, bool? Optional, bool? Repeated,
        string[]? Choices, string? PropertyKind);

    public static IEnumerable<Entry> Entries()
    {
        foreach (var f in StoryParser.Functions)
        {
            var d = f.Doc ?? throw new InvalidOperationException($"built-in '{f.FuncName}' has no documentation");
            var forms = f.Forms.Length == 0 ? null : f.Forms.Select(form => ToForm(f.FuncName, form)).ToArray();
            yield return new Entry("function", f.FuncName, d.Category, d.Signatures, d.Summary, d.Example,
                Forms: forms, Checked: f.IsChecked ? true : null);
        }

        foreach (var a in StoryParser.Attributes)
        {
            var targets = Enum.GetValues<AttributeTarget>().Where(t => a.Targets.HasFlag(t))
                .Select(t => t.ToString().ToLowerInvariant()).ToArray();
            var parameters = a.Params.Select(p => new Parameter(p.Name, Camel(p.Kind.ToString()), p.Describe(a.Params),
                p.Optional ? true : null, p.Repeated ? true : null, p.Choices, p.PropertyKind is { } k ? Camel(k.ToString()) : null)).ToArray();
            yield return new Entry("attribute", a.Name, a.Category, [a.Signature], a.Summary, a.Example, targets,
                parameters);
        }
    }

    static Form ToForm(string name, FunctionForm form) => form switch
    {
        CallForm c => new Form("call", c.Signature(name),
            c.Params.Select(p => new FormParameter(p.Name, Camel(p.Kind.ToString()), p.TypeText(c.Params),
                p.Optional ? true : null, p.Repeated ? true : null)).ToArray(),
            c.Returns == FnReturn.Nothing ? null : c.ReturnText),
        BindingForm b => new Form("binding", b.Signature(name), Returns: b.Returns == FnReturn.Nothing ? null : "number",
            Head: Camel(b.Head.ToString()), Block: b.Block),
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };

    static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
