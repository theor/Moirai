using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Moirai.Parser;
using NUnit.Framework;

namespace Moirai.LanguageServer.Tests;

// Pins the TextMate grammar (vscode-languageserver/moirai.tmLanguage.json) to the engine.
//
// The grammar is the colouring wherever the language server is not running: VS Code before the
// server's semantic tokens arrive, and the blog, whose shiki reads the same file. It cannot ask the
// tokenizer anything, so its word lists are copies -- and a keyword or builtin added to the language
// would otherwise go uncoloured there with nothing failing. Same idea as SyntaxHighlightingDriftTests
// for the LSP and moirai-language.test.ts for the Story page's editor.
public class TextMateGrammarDriftTests
{
    static readonly string GrammarPath =
        Path.Combine(Corpus.RepoRoot, "vscode-languageserver", "moirai.tmLanguage.json");

    static JsonObject Repository => (JsonObject)JsonNode.Parse(File.ReadAllText(GrammarPath))!["repository"]!;

    /// The words of a `\b(a|b|c)\b` rule.
    static IEnumerable<string> Words(JsonNode rule)
    {
        var match = Regex.Match((string)rule["match"]!, @"^\\b\(([\w|]+)\)\\b$");
        Assert.That(match.Success, $"not a word-list rule: {rule["match"]}");
        return match.Groups[1].Value.Split('|');
    }

    [Test]
    public void Keywords_are_exactly_the_tokenizers()
    {
        var inGrammar = ((JsonArray)Repository["keywords"]!["patterns"]!).SelectMany(p => Words(p!)).ToList();

        Assert.That(inGrammar, Is.Unique);
        Assert.That(inGrammar, Is.EquivalentTo(MoiraiTokenizer.ReservedWords.Keys));
    }

    [Test]
    public void Builtins_are_exactly_the_parsers_functions()
    {
        var inGrammar = Words(Repository["builtins"]!).ToList();

        Assert.That(inGrammar, Is.Unique);
        Assert.That(inGrammar, Is.EquivalentTo(StoryParser.Functions.Select(f => f.FuncName)));
    }

    /// Oniguruma, which shiki and VS Code run, is not .NET's engine, but the grammar sticks to syntax both
    /// read the same way. A pattern .NET rejects is a typo more often than an Oniguruma feature.
    [Test]
    public void Every_pattern_compiles()
    {
        var patterns = Repository.DescendantsAndSelf()
            .OfType<JsonObject>()
            .SelectMany(o => new[] { o["match"], o["begin"], o["end"] })
            .OfType<JsonValue>()
            .Select(v => (string)v!)
            .ToList();

        Assert.That(patterns, Is.Not.Empty);
        foreach (var pattern in patterns)
            Assert.DoesNotThrow(() => _ = new Regex(pattern), pattern);
    }
}

static class JsonNodeExtensions
{
    public static IEnumerable<JsonNode> DescendantsAndSelf(this JsonNode node)
    {
        yield return node;
        var children = node switch
        {
            JsonObject o => o.Select(kv => kv.Value),
            JsonArray a => a.AsEnumerable(),
            _ => [],
        };
        foreach (var child in children.OfType<JsonNode>())
            foreach (var d in child.DescendantsAndSelf())
                yield return d;
    }
}
