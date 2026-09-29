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

/// One `@name(...)` attribute. <see cref="StoryParser.Attributes"/> is the registry the parser checks
/// before lowering an attribute, so an attribute that is not documented here is not accepted either.
public sealed record AttributeDescriptor(string Name, AttributeTarget Targets, BuiltinDoc Doc);

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
        string Summary, string? Example, string[]? Targets);

    public static IEnumerable<Entry> Entries()
    {
        foreach (var f in StoryParser.Functions)
        {
            var d = f.Doc ?? throw new InvalidOperationException($"built-in '{f.FuncName}' has no documentation");
            yield return new Entry("function", f.FuncName, d.Category, d.Signatures, d.Summary, d.Example, null);
        }

        foreach (var a in StoryParser.Attributes)
        {
            var targets = Enum.GetValues<AttributeTarget>().Where(t => a.Targets.HasFlag(t))
                .Select(t => t.ToString().ToLowerInvariant()).ToArray();
            yield return new Entry("attribute", a.Name, a.Doc.Category, a.Doc.Signatures, a.Doc.Summary,
                a.Doc.Example, targets);
        }
    }
}
