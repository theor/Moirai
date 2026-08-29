using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Moirai.LanguageServer.Tests;

/// The behavioural specification for code completion.
///
/// Transcribed from the tests that covered the old antlr4-c3 implementation, with the expectations
/// restated as categories rather than ANTLR rule indices (RULE_type_id, RULE_var_id_read,
/// RULE_dot_property, RULE_fun_id) so they survive the change of engine. Each case asserts that the
/// suggestion list contains at least one item of the expected category -- the same shape of
/// assertion the originals made with Contains.Item.
///
/// The last two cases are the important ones: the caret sits in a definition that does not parse,
/// which is the normal situation while typing and the reason completion reads tokens rather than
/// the AST.
///
/// Database.Instance is a mutable static shared by the parse pipeline, so this fixture is serial.
[NonParallelizable]
public class CompletionSpec
{
    public enum Expect
    {
        /// A keyword, completed from the token the caret sits in (`pro|` -> `prop`).
        Keyword,
        /// Entity/enum/table type names (`create |`).
        TypeName,
        /// In-scope `$variables`, plus type names (`set |`).
        VariableOrType,
        /// Properties of the entity the path resolves to (`set $p.|`).
        Property,
        /// Built-in and user-defined function names, at the start of a statement.
        FunctionName,
    }

    const string PersonBirthPlace = @"
entity Person {
    prop birthplace: string

}

@start
event start {
    create Person $p: ('{random(Name)}')
    set $p.birthplace =  '{random(Name)}'

}
";

    const string SystemSource = @"entity System {
    prop asd: string
}

@start
event create_solar_system {
    create System $s: 'Solar System'
    create System $sys: ()
}
trigger born {
    when_created System
    set $
}
";

    public static IEnumerable<TestCaseData> Cases()
    {
        yield return Case(PersonBirthPlace, 2, 2, Expect.Keyword, "|  prop");
        yield return Case(PersonBirthPlace, 2, 4, Expect.Keyword, "  |prop");
        yield return Case(PersonBirthPlace, 2, 8, Expect.Keyword, "prop|");
        yield return Case(PersonBirthPlace, 8, 11, Expect.TypeName, "create |");
        yield return Case(PersonBirthPlace, 9, 4, Expect.Keyword, "|set");
        yield return Case(PersonBirthPlace, 9, 6, Expect.Keyword, "se|t");
        yield return Case(PersonBirthPlace, 9, 7, Expect.Keyword, "set|");
        yield return Case(PersonBirthPlace, 9, 8, Expect.VariableOrType, "set |");
        yield return Case(PersonBirthPlace, 9, 9, Expect.VariableOrType, "set $|");
        yield return Case(PersonBirthPlace, 9, 10, Expect.VariableOrType, "set $p|");
        yield return Case(PersonBirthPlace, 9, 11, Expect.Property, "set $p.|");
        yield return Case(PersonBirthPlace, 10, 0, Expect.FunctionName, "    |<func call>");
        // Both of these sit in a definition that does not parse -- the case the rewrite must handle.
        yield return Case(SystemSource, 11, 9, Expect.VariableOrType, "set $| in a broken trigger");
        yield return Case(SystemSource, 10, 6, Expect.Keyword, "when| (completes to when/when_created)");
    }

    static TestCaseData Case(string code, int line, int column, Expect expect, string name) =>
        new TestCaseData(code, line, column, expect).SetName($"({line}:{column}) '{name}' -> {expect}");

    static readonly DocumentUri Uri = new("file", null, "/completion.sg", null, null);

    static MoiraiDocument Process(string source)
    {
        var doc = new MoiraiDocument(Uri,
            new TextDocumentItem { Uri = Uri, LanguageId = "moirai", Text = source, Version = 1 });
        doc.Process(new FakeLogger<MoiraiCache>()).GetAwaiter().GetResult();
        return doc;
    }

    /// `VariableOrType` accepts either, because that position takes both and which one exists
    /// depends on whether the enclosing definition parsed.
    static bool Matches(Expect expect, CompletionItemKind kind) => expect switch
    {
        Expect.Keyword => kind == CompletionItemKind.Keyword,
        Expect.TypeName => kind is CompletionItemKind.Class or CompletionItemKind.Enum,
        Expect.VariableOrType => kind is CompletionItemKind.Variable or CompletionItemKind.Class,
        Expect.Property => kind is CompletionItemKind.Property or CompletionItemKind.Method,
        Expect.FunctionName => kind == CompletionItemKind.Function,
        _ => false,
    };

    [Test, TestCaseSource(nameof(Cases))]
    public void Completion_offers_the_right_category(string code, int line, int column, Expect expect)
    {
        var items = MoiraiCompletion.Complete(Process(code), new Position(line, column));

        Assert.That(items.Any(i => Matches(expect, i.Kind)), Is.True,
            () => $"expected at least one {expect} suggestion, got: " +
                  (items.Count == 0
                      ? "(nothing)"
                      : string.Join(", ", items.Select(i => $"{i.Label}:{i.Kind}").Take(30))));
    }

    /// The category tests above prove the caret was understood; these prove the suggestions are
    /// actually the user's own symbols rather than a generic list.
    [Test]
    public void Property_completion_offers_the_receivers_own_properties()
    {
        var items = MoiraiCompletion.Complete(Process(PersonBirthPlace), new Position(9, 11));
        Assert.That(items.Select(i => i.Label), Does.Contain("birthplace"));
    }

    [Test]
    public void Type_completion_offers_declared_types()
    {
        var items = MoiraiCompletion.Complete(Process(PersonBirthPlace), new Position(8, 11));
        Assert.That(items.Select(i => i.Label), Does.Contain("Person"));
    }

    [Test]
    public void Statement_completion_offers_builtin_functions()
    {
        var items = MoiraiCompletion.Complete(Process(PersonBirthPlace), new Position(10, 0));
        var labels = items.Select(i => i.Label).ToList();
        Assert.That(labels, Does.Contain("record"));
        Assert.That(labels, Does.Contain("create"));
    }

    /// The case the old engine could not reach: a variable declared in a definition that does not
    /// parse is recovered from the token stream.
    [Test]
    public void Variables_from_an_unparsed_definition_are_still_offered()
    {
        const string src = "entity Person {\n    prop age: number\n}\n\n@start\nevent e {\n    create Person $hero: 'x'\n    set $\n}\n";
        var items = MoiraiCompletion.Complete(Process(src), new Position(7, 9));
        Assert.That(items.Select(i => i.Label), Does.Contain("$hero"),
            () => string.Join(", ", items.Select(i => i.Label)));
    }

    /// Nothing should be suggested in the middle of prose.
    [Test]
    public void No_suggestions_inside_a_string_literal()
    {
        const string src = "@start\nevent e {\n    record('hello world')\n}\n";
        var items = MoiraiCompletion.Complete(Process(src), new Position(2, 18));
        Assert.That(items, Is.Empty, () => string.Join(", ", items.Select(i => i.Label)));
    }

    /// ...but an interpolation hole is code again.
    [Test]
    public void Suggestions_inside_an_interpolation_hole()
    {
        const string src = "@start\nevent e {\n    record('hi {}')\n}\n";
        var items = MoiraiCompletion.Complete(Process(src), new Position(2, 16));
        Assert.That(items, Is.Not.Empty);
    }

    /// Completion must not throw on input the parser cannot make sense of at all.
    [Test]
    public void Does_not_throw_on_garbage()
    {
        const string src = "entity {{{ $ . @ ??\n";
        var doc = Process(src);
        for (int col = 0; col <= src.IndexOf('\n'); col++)
            Assert.DoesNotThrow(() => MoiraiCompletion.Complete(doc, new Position(0, col)));
    }
}
