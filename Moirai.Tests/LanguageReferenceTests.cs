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
    [TestCase("@display('Kin', each Person $p: (parent1 = $self))\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.UnknownCall)]
    [TestCase("@start\nentity Kin {\n    prop age: number\n}", StoryParser.ErrorCode.UnknownAttribute)]
    public void AnAttributeTheRegistryDoesNotListForThatDefinitionIsAnError(string definition, StoryParser.ErrorCode code)
    {
        StoryParser.Parse(Prelude + definition + "\n", out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(code), () => string.Join("\n", errors));
    }

    /// An attribute's signature is generated from its parameters. These pin the notation: choices joined
    /// by `|`, an optional parameter in brackets, a repeated one followed by `...`, no parentheses for none.
    [TestCase("frequency", AttributeTarget.Event, "@frequency(x, PerXYear | EveryXYear, y)")]
    [TestCase("display", AttributeTarget.Type, "@display('Label', each T $v: (predicate...)[, 'item format'])")]
    [TestCase("tag", AttributeTarget.Trigger, "@tag('name', ...)")]
    [TestCase("parents", AttributeTarget.Type, "@parents(a, b)")]
    [TestCase("start", AttributeTarget.Event, "@start")]
    public void AnAttributesSignatureIsGeneratedFromItsParameters(string name, AttributeTarget target, string signature) =>
        Assert.That(StoryParser.GetAttribute(name, target)!.Signature, Is.EqualTo(signature));

    [Test]
    public void OnlyAnAttributesLastParameterIsOptionalOrRepeated()
    {
        foreach (var a in StoryParser.Attributes)
            Assert.That(a.Params.SkipLast(1).Any(p => p.Optional || p.Repeated), Is.False, a.Name);
    }

    /// Every attribute's arguments go through one binder. Each case is a mistake it catches, with the
    /// error code the attribute's own checks used before they were shared; the first used to read an
    /// argument that was not there.
    [TestCase("@frequency(1)\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.MissingArgument)]
    [TestCase("@frequency(1, PerXYear, 2, 3)\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@frequency(1, Weekly, 2)\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.UnknownEnum)]
    [TestCase("@frequency(1, Job.Farmer, 2)\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.UnknownEnum)]
    [TestCase("@frequency(0, EveryXYear, 2)\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@frequency(1.5, PerXYear, 2)\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@start(1)\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@tag\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.MissingArgument)]
    [TestCase("@tag(war)\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@tag('{$x}')\nevent e {\n    record('x')\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@display('Kin')\nentity Kin {\n    prop age: number\n}", StoryParser.ErrorCode.MissingArgument)]
    [TestCase("@display('Kin', each Nobody $n: (age > 1))\nentity Kin {\n    prop age: number\n}", StoryParser.ErrorCode.UnknownEntityType)]
    [TestCase("@display('Kin', each Kin $k: (age > 1), 4)\nentity Kin {\n    prop age: number\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@display('Kin', age > 1)\nentity Kin {\n    prop age: number\n}", StoryParser.ErrorCode.InvalidArgument)]
    public void AnAttributesArgumentsAreCheckedAgainstItsParameters(string definition, StoryParser.ErrorCode code)
    {
        StoryParser.Parse(Prelude + definition + "\n", out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(code), () => string.Join("\n", errors));
    }

    /// The old @display named the type first and bound an implicit $other; the error writes the new form.
    [Test]
    public void TheOldDisplayFormIsAnErrorThatSaysWhatToWrite()
    {
        StoryParser.Parse(Prelude + "@display(Kin, 'Kids', parent1 = $self, '{$other.name}')\nentity Kin {\n    prop parent1: Kin\n}\n", out var errors);
        Assert.That(errors.Select(e => e.Message),
            Has.Some.Contains("write @display('Kids', each Kin $other: (parent1 = $self), '{$other.name}')"));
    }

    [Test]
    public void AChoiceMayBeQualifiedByItsEnum()
    {
        StoryParser.Parse(Prelude + "@frequency(1, Frequency.EveryXYear, 2)\nevent e {\n    record('x')\n}\n", out var errors);
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors));
    }

    // ---- Built-in functions ----------------------------------------------------------------------

    /// A built-in's signatures are generated from its forms. These pin the notation.
    [TestCase("floor", 0, "floor(x: number): number")]
    [TestCase("record", 0, "record('text'[, weight: number])")]
    [TestCase("random", 0, "random(E: enum): E")]
    [TestCase("random", 2, "random(min: number, max: number): number")]
    [TestCase("related", 0, "related(a: entity, b: entity, n: 0..6): bool")]
    [TestCase("add", 0, "add(coll: collection, value: element of coll)")]
    [TestCase("debug", 0, "debug(value: any, ...)")]
    [TestCase("roll", 0, "roll(T: table): entry of T")]
    [TestCase("pick", 1, "pick T $v[: (predicate...)] else { ... }")]
    [TestCase("sum", 0, "sum T $v: ([predicate..., ]value: number): number")]
    [TestCase("count", 1, "count(coll: collection): number")]
    [TestCase("create", 3, "create T $v: 'name' { prop := value ... }")]
    [TestCase("each", 0, "each T $v[: (predicate...)] { ... }")]
    [TestCase("chance", 1, "chance(p: number) { ... }")]
    [TestCase("schedule", 0, "schedule(entity: entity, year: number) { ... }")]
    public void AFunctionsSignatureIsGeneratedFromItsForms(string name, int form, string signature)
    {
        StoryParser.GetFunctionDescriptor(name, out var f);
        Assert.That(f!.Doc!.Signatures[form], Is.EqualTo(signature));
    }

    /// Every built-in is checked against its forms by the parser -- plain calls, binding forms and the
    /// blocks either carries -- and its signatures are generated from them: nothing in the reference is
    /// hand-written. Adding a built-in means giving it forms.
    [Test]
    public void EveryBuiltinHasForms()
    {
        Assert.That(StoryParser.Functions.Where(f => f.Forms.Length == 0).Select(f => f.FuncName), Is.Empty);
    }

    [Test]
    public void OnlyAFormsLastParameterIsOptionalOrRepeated()
    {
        foreach (var f in StoryParser.Functions)
        foreach (var form in f.Forms.OfType<CallForm>())
        {
            Assert.That(form.Params.SkipLast(1).Any(p => p.Optional || p.Repeated), Is.False, f.FuncName);
            foreach (var p in form.Params.Where(p => p.Constraint == ValueConstraint.ElementOf))
                Assert.That(form.Params[p.Of].Kind, Is.EqualTo(FnArgKind.Collection), f.FuncName);
        }
    }

    /// Every checked built-in's arguments go through one binder. Each case is a mistake it catches; most
    /// were accepted before, and the last used to crash the parser.
    [TestCase("pick Person $p: (alive)\nvar $x = floor($p.name)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("var $x = floor()", StoryParser.ErrorCode.MissingArgument)]
    [TestCase("var $x = floor(1, 2)", StoryParser.ErrorCode.MissingArgument)]
    [TestCase("pick Person $p: (alive)\nadd($p.friends, Job.Farmer)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("pick Person $p: (alive)\nadd($p.age, $p)", StoryParser.ErrorCode.ExpectedCollection)]
    [TestCase("var $x = random('x')", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("var $x = random(true)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("var $x = random(Job.Farmer)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("pick Person $x: (alive)\nvar $r = related($x, $x, 9)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("pick Person $x: (alive)\nvar $r = related($x, 3, 2)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("var $x = roll(Nope)", StoryParser.ErrorCode.UnknownTable)]
    [TestCase("record(3)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("var $x = not(3)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("pick Person $p: (alive)\nrecord('{link(3, 'x')}')", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("floor(1) {\n    record('x')\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("chance(5%) else {\n    record('y')\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("each Person $p: (alive)", StoryParser.ErrorCode.MissingEachScope)]
    [TestCase("pick Person $p: (alive) {\n    record('x')\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("pick Person $p: (alive)\nschedule($p, 3)", StoryParser.ErrorCode.MissingEachScope)]
    [TestCase("pick Person $p: (alive)\nschedule(3, 3) {\n    record('x')\n}", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("var $x = sum Person $p: (alive)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("var $x = sum(3)", StoryParser.ErrorCode.MissingVariable)]
    [TestCase("var $x = floor Person $p", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("create Person $p: 3", StoryParser.ErrorCode.InvalidArgument)]
    public void ABuiltinsArgumentsAreCheckedAgainstItsForms(string body, StoryParser.ErrorCode code)
    {
        var story = Prelude + "event e {\n" + Indent(body) + "\n}\n";
        StoryParser.Parse(story, out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(code), () => string.Join("\n", errors));
    }

    [Test]
    public void AMarkInAFunctionIsAnErrorNotACrash()
    {
        List<StoryParser.Error> errors = null!;
        Assert.DoesNotThrow(() => StoryParser.Parse(Prelude + "function f($p: Person) {\n    mark($p)\n}\n", out errors));
        Assert.That(errors.Select(e => e.Message), Has.Some.Contains("only in an event or a trigger"));
    }

    /// Which form a call binds to is decided by its arguments' shape when two forms take the same count.
    [TestCase("var $x = random(Job)")]
    [TestCase("var $x = random(10)")]
    [TestCase("var $x = random(count Person $p: (alive))")]
    [TestCase("var $x = random(2, 3 + 4)")]
    public void AFormIsChosenByItsArgumentsShape(string body)
    {
        StoryParser.Parse(Prelude + "event e {\n" + Indent(body) + "\n}\n", out var errors);
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors));
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
                if (e.TryGetProperty("forms", out var forms))
                {
                    // Each distinct block once: what it is for, when it runs and what it sees.
                    var blocks = forms.EnumerateArray()
                        .SelectMany(f => f.TryGetProperty("blocks", out var bs) ? bs.EnumerateArray() : [])
                        .Select(bl => (Keyword: bl.TryGetProperty("keyword", out var k) ? k.GetString() + " " : "",
                            Describes: bl.GetProperty("describes").GetString()))
                        .Distinct().ToList();
                    foreach (var (keyword, describes) in blocks)
                        b.Append("- `").Append(keyword).Append("{ ... }`: ").Append(describes).Append(".\n");
                    if (blocks.Count > 0)
                        b.Append('\n');
                }

                if (e.TryGetProperty("parameters", out var parameters) && parameters.GetArrayLength() > 0)
                {
                    foreach (var p in parameters.EnumerateArray())
                    {
                        var flags = new List<string>();
                        if (p.TryGetProperty("optional", out _)) flags.Add("optional");
                        if (p.TryGetProperty("repeated", out _)) flags.Add("one or more");
                        b.Append("- `").Append(p.GetProperty("name").GetString()).Append("`: ")
                            .Append(p.GetProperty("accepts").GetString())
                            .Append(flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "")
                            .Append(".\n");
                    }

                    b.Append('\n');
                }

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
