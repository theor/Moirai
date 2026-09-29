using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moirai.Parser;

namespace Moirai.Tests;

/// The language reference is generated, in two steps. The built-ins' and attributes' documentation lives
/// beside their code (StoryParser.Functions and StoryParser.Attributes) and is written out as
/// docs/language-reference.json; everything a reader sees -- docs/language-reference.md here, the Story
/// page's hover and completion in the browser -- is rendered from that JSON, never from the C# directly.
/// The JSON is also copied into the client, which cannot import from outside its own folder.
///
/// Re-generate both after changing a doc with:  UPDATE_GOLDENS=1 dotnet test Moirai.Tests
public class LanguageReferenceTests
{
    const string JsonPath = "docs/language-reference.json";
    const string ClientJsonPath = "MoiraiWebServer/ClientAppSvelte/src/lib/language-reference.json";
    const string MarkdownPath = "docs/language-reference.md";

    /// What every example runs against: a small world with enough in it for any built-in to have
    /// something to work on. A function's example becomes the body of an event; an attribute's example is
    /// appended as top-level definitions.
    const string Prelude = @"enum Job { Farmer, Smith, Bard }
table Name {
    'Ada', 'Bran', 'Cora'
}
entity Person {
    prop age: number
    prop alive: bool
    prop wealth: number
    prop happiness: percentage
    prop job: Job
    prop partner: Person
    prop parent1: Person
    prop parent2: Person
    prop friends: [Person]
}
event harvest {
    record('A good harvest')
}
event greet($who: Person) {
    record('{$who.name} is greeted')
}
function feast() {
    record('A feast')
}
";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// The whole reference, sections in order and each section's entries by name, so an entry's place in
    /// the file never depends on where in the code it was registered.
    static string ReferenceJson() =>
        JsonSerializer.Serialize(new
        {
            sections = LanguageReference.Sections.Select(s => new { id = s.Category, title = s.Title }),
            entries = LanguageReference.Entries()
                .OrderBy(e => e.Category).ThenBy(e => e.Kind).ThenBy(e => e.Name, StringComparer.Ordinal),
        }, JsonOptions).Replace("\r\n", "\n") + "\n";

    static IEnumerable<TestCaseData> Examples() =>
        LanguageReference.Entries().Select(e =>
            new TestCaseData(e.Kind, e.Name, e.Example ?? "").SetName($"{e.Kind} {e.Name}"));

    [Test]
    public void EveryBuiltinFunctionIsDocumented()
    {
        var missing = StoryParser.Functions
            .Where(f => f.Doc is not { Summary.Length: > 0, Signatures.Length: > 0, Example.Length: > 0 })
            .Select(f => f.FuncName).ToList();
        Assert.That(missing, Is.Empty, "built-ins without a summary, a signature and an example");
    }

    [Test]
    public void EveryAttributeIsDocumented()
    {
        var missing = StoryParser.Attributes
            .Where(a => a.Doc is not { Summary.Length: > 0, Signatures.Length: > 0, Example.Length: > 0 })
            .Select(a => a.Name).ToList();
        Assert.That(missing, Is.Empty, "attributes without a summary, a signature and an example");
    }

    [Test]
    public void EverySignatureNamesWhatItDocuments()
    {
        foreach (var e in LanguageReference.Entries())
        {
            var name = e.Kind == "attribute" ? "@" + e.Name : e.Name;
            foreach (var signature in e.Signatures)
                Assert.That(signature, Does.StartWith(name), $"{e.Kind} {e.Name}");
        }
    }

    /// A definition that fails to parse is dropped without a trace, so "it parsed" is not enough: an
    /// example must produce no error at all.
    [TestCaseSource(nameof(Examples))]
    public void TheExampleParsesCleanly(string kind, string name, string example)
    {
        var story = kind == "attribute"
            ? Prelude + example + "\n"
            : Prelude + "event example {\n" + Indent(example) + "\n}\n";

        StoryParser.Parse(story, out var errors);
        Assert.That(errors.Where(e => e.Severity == StoryParser.Severity.Error), Is.Empty,
            () => story + "\n" + string.Join("\n", errors));
        Assert.That(example, Does.Contain(kind == "attribute" ? "@" + name : name + " ").Or.Contain(name + "("),
            "the example should use what it documents");
    }

    static string Indent(string code) =>
        string.Join("\n", code.Split('\n').Select(l => l.Length == 0 ? l : "    " + l));

    [TestCase("@lineage(mother)\nentity Kin {\n    prop mother: Kin\n}", StoryParser.ErrorCode.UnknownAttribute)]
    [TestCase("@shiny\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.UnknownCall)]
    [TestCase("@frequency(1, PerXYear, 2)\ntrigger t {\n    when_created Person\n}", StoryParser.ErrorCode.UnknownCall)]
    [TestCase("@display(Person, 'Kin', parent1 = $self)\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.UnknownCall)]
    [TestCase("@start\nentity Kin {\n    prop age: number\n}", StoryParser.ErrorCode.UnknownAttribute)]
    public void AnAttributeTheRegistryDoesNotListForThatDefinitionIsAnError(string definition, StoryParser.ErrorCode code)
    {
        StoryParser.Parse(Prelude + definition + "\n", out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(code), () => string.Join("\n", errors));
    }

    /// The hand-written guide opens on a whole story; a reader will paste it, so it has to work.
    [Test]
    public void TheGuidesFirstStoryParsesCleanly()
    {
        var guide = File.ReadAllText(Path.Combine(Golden.RepoRoot, "docs", "language.md")).Replace("\r\n", "\n");
        var story = guide.Split("```moirai\n")[1].Split("```")[0];
        StoryParser.Parse(story, out var errors);
        Assert.That(errors.Where(e => e.Severity == StoryParser.Severity.Error), Is.Empty,
            () => string.Join("\n", errors));
    }

    [Test]
    public void TheJsonReferenceIsUpToDate()
    {
        var json = ReferenceJson();
        Golden.VerifyFile(JsonPath, json);
        Golden.VerifyFile(ClientJsonPath, json);
    }

    /// Rendered from the JSON rather than from the table -- the same text TheJsonReferenceIsUpToDate pins
    /// to disk -- so the page is a function of the one file every other reader uses, and one re-bless
    /// regenerates both.
    [Test]
    public void TheMarkdownReferenceIsUpToDate() => Golden.VerifyFile(MarkdownPath, RenderMarkdown(ReferenceJson()));

    static string RenderMarkdown(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var entries = root.GetProperty("entries").EnumerateArray().ToList();
        var b = new StringBuilder();
        b.Append("# Moirai language reference\n\n");
        b.Append("<!-- Generated from language-reference.json by Moirai.Tests/LanguageReferenceTests.cs. Do not edit: ")
            .Append("change the documentation in Moirai.Parser/StoryParser.cs, then run `UPDATE_GOLDENS=1 dotnet test Moirai.Tests`. -->\n\n");
        b.Append("Every built-in function and attribute of the `.sg` language. For how a story is put together, ")
            .Append("read the [language guide](language.md) first.\n\n");

        var sections = root.GetProperty("sections").EnumerateArray()
            .Select(s => (Id: s.GetProperty("id").GetString()!, Title: s.GetProperty("title").GetString()!))
            .Where(s => entries.Any(e => e.GetProperty("category").GetString() == s.Id))
            .ToList();

        foreach (var (id, title) in sections)
            b.Append("- [").Append(title).Append("](#").Append(Anchor(title)).Append(")\n");
        b.Append('\n');

        foreach (var (id, title) in sections)
        {
            b.Append("## ").Append(title).Append("\n\n");
            foreach (var e in entries.Where(e => e.GetProperty("category").GetString() == id))
            {
                var isAttribute = e.GetProperty("kind").GetString() == "attribute";
                b.Append("### `").Append(isAttribute ? "@" : "").Append(e.GetProperty("name").GetString()).Append("`\n\n");
                b.Append("```moirai\n");
                foreach (var s in e.GetProperty("signatures").EnumerateArray())
                    b.Append(s.GetString()).Append('\n');
                b.Append("```\n\n");
                b.Append(e.GetProperty("summary").GetString()).Append("\n\n");
                if (e.TryGetProperty("targets", out var targets))
                    b.Append("Applies to: ")
                        .Append(string.Join(", ", targets.EnumerateArray().Select(t => t.GetString() + "s")))
                        .Append(".\n\n");
                if (e.TryGetProperty("example", out var example))
                    b.Append("```moirai\n").Append(example.GetString()).Append("\n```\n\n");
            }
        }

        return b.ToString().TrimEnd('\n') + "\n";
    }

    /// GitHub's heading anchors: lower case, spaces to dashes, punctuation dropped.
    static string Anchor(string title) =>
        new string(title.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-').ToArray())
            .Replace(' ', '-');
}
