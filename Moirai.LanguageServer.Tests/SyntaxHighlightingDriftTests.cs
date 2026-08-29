using Microsoft.Extensions.Logging.Testing;
using Moirai.Parser;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Moirai.LanguageServer.Tests;

// Guards the LSP semantic-highlighter against grammar drift.
//
// These tests were written when highlighting was an ANTLR parse-tree visitor, where a newly added
// keyword was silently walked past: parsing kept working, the keyword was never coloured, and
// nothing failed. The current highlighter colours keywords straight from the token kind, which
// makes that particular gap structurally impossible -- but the tests are kept, retargeted at
// MoiraiTokenizer, because they now guard the same property one level up:
//
//   1. Completeness  - every reserved word the tokenizer knows must appear in Corpus below.
//                      Add a keyword to MoiraiTokenizer and this fails until you give it an example.
//   2. Coverage      - every keyword occurrence in Corpus must be emitted as a semantic token.
//
// So a new language feature can't ship a green build without either highlighting its keyword or
// consciously exempting it (see ExemptKeywords).
[NonParallelizable]
public class SyntaxHighlightingDriftTests
{
    // Intentionally exercises every keyword the lexer defines. Keep it parseable (Corpus_parses_clean
    // asserts zero errors) — the visitor skips subtrees of a broken parse, which would mask coverage gaps.
    private const string Corpus = @"entity Country {
    prop health: number
    prop title: string
    prop ally: Country
    function score(): number {
        $self.health * 2
    }
}

enum Mood {
    Happy,
    Sad
}

table Greeting {
    70 => 'Hello',
    'Hi'
}

singleton World {
    prop turn: number
}

function global_score($c: Country): number {
    $c.health * 3
}

@start
event start {
    create Country $c: ('Test')
    set $c.health = 10
    var $flag: true
    if $c.health > 5 and $c.health < 100 or false {
        record('big')
    } else {
        record('small')
    }
    match $c.health {
        1 => record('one')
        2 => random_weighted 100 {
            3 => set $c.health = 1
        }
    }
    if $c.ally = null {
        record('alone')
    }
}

trigger born {
    when_created Country
    record('made {$new.name}')
}

trigger grows {
    when Country and $new.health > 0
    set $new.health = $new.health
}
";

    // Keyword literals deliberately NOT highlighted as keywords. Documented here so the
    // exemption is a conscious choice rather than a silent gap. (Currently empty: every keyword is highlighted.)
    private static readonly HashSet<string> ExemptKeywords = new();

    private static readonly DocumentUri Uri = new("file", null, "/highlight-corpus.sg", null, null);

    // The language's reserved words, straight from the tokenizer's own table.
    private static List<string> Keywords() =>
        MoiraiTokenizer.ReservedWords.Keys.Where(w => !ExemptKeywords.Contains(w)).ToList();

    private static HashSet<MoiraiTokenKind> KeywordKinds() =>
        MoiraiTokenizer.ReservedWords
            .Where(kv => !ExemptKeywords.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToHashSet();

    // All occurrences (0-based line, 0-based column) of keyword tokens in source. Superpower
    // positions are 1-based on both axes; LSP is 0-based on both.
    private static List<(string word, int line, int col)> KeywordOccurrences(string source)
    {
        var kinds = KeywordKinds();
        var occurrences = new List<(string, int, int)>();
        foreach (var tok in MoiraiTokenizer.Tokenize(source).FullTokens)
            if (kinds.Contains(tok.Kind))
                occurrences.Add((tok.ToStringValue(), tok.Position.Line - 1, tok.Position.Column - 1));
        return occurrences;
    }

    private static MoiraiDocument Process(string source)
    {
        var doc = new MoiraiDocument(Uri,
            new TextDocumentItem { Uri = Uri, LanguageId = "moirai", Text = source, Version = 1 });
        doc.Process(new FakeLogger<MoiraiCache>()).GetAwaiter().GetResult();
        return doc;
    }

    [Test]
    public void Corpus_parses_clean()
    {
        var doc = Process(Corpus);
        Assert.That(doc.Errors, Is.Empty,
            () => "Corpus must parse cleanly or whole definitions drop out of the AST:\n" +
                  string.Join("\n", doc.Errors.Select(e => $"  {e.Code}: {e.Message}")));
    }

    [Test]
    public void Corpus_exercises_every_grammar_keyword()
    {
        var present = KeywordOccurrences(Corpus).Select(o => o.word).ToHashSet();
        var missing = Keywords().Where(w => !present.Contains(w)).ToList();
        Assert.That(missing, Is.Empty,
            () => "These grammar keywords have no example in Corpus — add one (and make sure they are " +
                  "highlighted), or add to ExemptKeywords: " + string.Join(", ", missing));
    }

    // `:=` is punctuation (not a bare-word keyword), so the keyword drift tests don't cover it.
    // This pins that the object-initializer operator and its property are highlighted.
    [Test]
    public void Init_colon_equals_is_highlighted()
    {
        const string src = "entity T {\n    prop a: number\n}\nevent e {\n    create T $t: 'x' {\n        a := 5\n    }\n}\n";
        var doc = Process(src);
        Assert.That(doc.Errors, Is.Empty, () => string.Join("\n", doc.Errors.Select(e => e.Message)));

        var colonEq = MoiraiTokenizer.Tokenize(src).FullTokens
            .Single(t => t.Kind == MoiraiTokenKind.ColonEq);
        var highlighted = doc.SemanticTokens
            .Select(t => (t.range.Start.Line, t.range.Start.Character))
            .ToHashSet();

        Assert.That(highlighted, Does.Contain((colonEq.Position.Line - 1, colonEq.Position.Column - 1)),
            "the := initializer operator must be emitted as a semantic token");
    }

    [Test]
    public void Every_keyword_occurrence_is_highlighted()
    {
        var doc = Process(Corpus);
        var highlighted = doc.SemanticTokens
            .Select(t => (t.range.Start.Line, t.range.Start.Character))
            .ToHashSet();

        var gaps = KeywordOccurrences(Corpus)
            .Where(o => !highlighted.Contains((o.line, o.col)))
            .ToList();

        Assert.That(gaps, Is.Empty,
            () => "No semantic token is emitted for these keyword occurrences (highlighting is " +
                  "out of sync with the grammar):\n" +
                  string.Join("\n", gaps.Select(g => $"  '{g.word}' at {g.line}:{g.col}")));
    }
}
