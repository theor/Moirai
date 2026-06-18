using Antlr4.Runtime;
using Microsoft.Extensions.Logging.Testing;
using Moirai.Parser;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Antlr4CodeCompletion.CoreUnitTest.CodeCompletion;

// Guards the LSP semantic-highlighter (TokenVisitor) against grammar drift.
//
// TokenVisitor derives from MoiraiParserBaseVisitor, so when the grammar gains a new keyword the
// base visitor silently walks past it: parsing keeps working, but the keyword is never highlighted
// and nothing fails. These tests make that failure loud:
//
//   1. Completeness  - every keyword token defined in the lexer must appear in Corpus below.
//                      Add a keyword to moirai_lexer.g4 and this fails until you give it an example.
//   2. Coverage      - every keyword occurrence in Corpus must be emitted as a semantic token.
//                      The example exists but TokenVisitor doesn't highlight it -> this fails.
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

    // Keyword literals deliberately NOT highlighted as keywords by TokenVisitor. Documented here so the
    // exemption is a conscious choice rather than a silent gap. (Currently empty: every keyword is highlighted.)
    private static readonly HashSet<string> ExemptKeywords = new();

    private static readonly DocumentUri Uri = new("file", null, "/highlight-corpus.sg", null, null);

    // Token types whose literal name is a bare word (e.g. 'event', 'when_created') — the language keywords.
    private static List<(int type, string text)> KeywordTokenTypes()
    {
        var lexer = new moirai_lexer(new AntlrInputStream(""));
        var vocab = lexer.Vocabulary;
        var result = new List<(int, string)>();
        for (int t = 1; t <= lexer.Atn.maxTokenType; t++)
        {
            var literal = vocab.GetLiteralName(t);
            if (literal is null || literal.Length < 3)
                continue;
            var word = literal.Trim('\'');
            if (IsBareWord(word) && !ExemptKeywords.Contains(word))
                result.Add((t, word));
        }

        return result;
    }

    private static bool IsBareWord(string s)
    {
        if (s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_'))
            return false;
        foreach (var ch in s)
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return false;
        return true;
    }

    // All occurrences (0-based line, 0-based column) of keyword tokens in source.
    private static List<(string word, int line, int col)> KeywordOccurrences(string source)
    {
        var keywordTypes = KeywordTokenTypes().ToDictionary(k => k.type, k => k.text);
        var lexer = new moirai_lexer(new AntlrInputStream(source));
        var occurrences = new List<(string, int, int)>();
        foreach (var tok in lexer.GetAllTokens())
            if (keywordTypes.TryGetValue(tok.Type, out var word))
                occurrences.Add((word, tok.Line - 1, tok.Column));
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
            () => "Corpus must parse cleanly or the visitor skips subtrees:\n" +
                  string.Join("\n", doc.Errors.Select(e => $"  {e.Code}: {e.Message}")));
    }

    [Test]
    public void Corpus_exercises_every_grammar_keyword()
    {
        var present = KeywordOccurrences(Corpus).Select(o => o.word).ToHashSet();
        var missing = KeywordTokenTypes().Select(k => k.text).Where(w => !present.Contains(w)).ToList();
        Assert.That(missing, Is.Empty,
            () => "These grammar keywords have no example in Corpus — add one (and highlight them in " +
                  "TokenVisitor), or add to ExemptKeywords: " + string.Join(", ", missing));
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
            () => "TokenVisitor emits no semantic token for these keyword occurrences (highlighting is " +
                  "out of sync with the grammar):\n" +
                  string.Join("\n", gaps.Select(g => $"  '{g.word}' at {g.line}:{g.col}")));
    }
}
