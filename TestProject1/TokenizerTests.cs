using Moirai.Parser;

namespace TestProject1;

/// Tokenizer cover: a snapshot of the token stream over the corpus and the hand-picked fixtures,
/// plus the standalone behaviours the LSP depends on.
///
/// Replaces TokenizerDifferentialTests, which asserted the same streams token-by-token against the
/// ANTLR lexer. The fixture list is kept verbatim, because each entry was chosen to pin a
/// non-obvious lexing decision the original scanner made -- sign folded into numeric literals,
/// `5.` splitting into NUMBER and DOT rather than a float, nested and escaped interpolation, and
/// ordinary braces being mode-stack-identical to interpolation holes.
public class TokenizerTests : TestsBase
{
    /// One line per token: kind, exact text, and 1-based line/column. Escaped so a newline or a run
    /// of spaces is visible in a diff rather than silently reshaping the snapshot.
    static string Serialize(string content)
    {
        var result = MoiraiTokenizer.Tokenize(content);
        var lines = result.FullTokens.ToArray().Select(t =>
            $"{t.Position.Line}:{t.Position.Column} {t.Kind} '{Escape(t.ToStringValue())}'");
        var errors = result.Errors.Select(e => $"error {e.Position.Line}:{e.Position.Column} {e.Message}");
        return string.Join("\n", lines.Concat(errors)) + "\n";
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");

    [Test]
    [TestCaseSource(typeof(Golden), nameof(Golden.Corpus))]
    public void CorpusFile_TokenStreamIsStable(string relativePath) =>
        Golden.Verify(relativePath.Replace('/', '_') + ".tokens", Serialize(Golden.Read(relativePath)));

    [TestCase("event e { record '{$a} and {$b}' }")]
    [TestCase("event e { record '{random(Name)} of {$c.name}' }")]
    [TestCase("event e { record '{func('nested {$x}')}' }")]
    [TestCase("event e { record 'a\\'b' }")] // escaped quote inside a string
    [TestCase("event e { record '' }")] // empty string (zero-length TEXT run — no TEXT token at all)
    [TestCase("event e { create Type $t: 'x' { p := 1 } }")] // ordinary '{'/'}', not interpolation
    [TestCase("2 + 3 * 4")]
    [TestCase("-2 + 3")]
    [TestCase("-4 - -3")]
    [TestCase("4-3")]
    [TestCase("$x - 1")]
    [TestCase("50%")]
    [TestCase("-50%")]
    [TestCase("2.1 + 3.2")]
    [TestCase("-2.1")]
    [TestCase("5.")] // dot not followed by a digit -> NUMBER "5" then DOT, not NUMBER_FLOAT
    public void Fixture_TokenStreamIsStable(string content) =>
        Golden.Verify("fixtures/" + Slug(content) + ".tokens", Serialize(content));

    /// A readable but unambiguous filename per fixture.
    ///
    /// The hash suffix is not decoration: squashing punctuation to underscores maps `-4 - -3` and
    /// `4-3` (and `50%` and `-50%`) onto the same name, and those are precisely the pairs these
    /// fixtures exist to tell apart -- they would have shared one snapshot and stopped testing
    /// anything. It is FNV-1a rather than string.GetHashCode() because .NET randomises string
    /// hashing per process, which would rename every golden on every run.
    static string Slug(string content)
    {
        var chars = content.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var slug = new string(chars).Trim('_');
        while (slug.Contains("__"))
            slug = slug.Replace("__", "_");
        if (slug.Length > 48)
            slug = slug[..48].TrimEnd('_');
        return $"{slug}_{Fnv1a(content):x8}";
    }

    static uint Fnv1a(string s)
    {
        uint hash = 2166136261;
        foreach (var c in s)
            hash = (hash ^ c) * 16777619;
        return hash;
    }

    // ---- Behaviours the language server relies on -----------------------------------------

    [Test]
    public void UnbalancedClosingBrace_DoesNotThrow_RecordsError()
    {
        Assert.DoesNotThrow(() => MoiraiTokenizer.Tokenize("event e { } }"));
        var result = MoiraiTokenizer.Tokenize("event e { } }");
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(result.Errors[0].Message, Does.Contain("Unbalanced"));
    }

    [Test]
    public void UnterminatedString_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => MoiraiTokenizer.Tokenize("event e { record 'no closing quote"));
    }

    [Test]
    public void FullTokens_IncludesSpaceAndComment_ParseTokensExcludesThem()
    {
        var result = MoiraiTokenizer.Tokenize("event e {} // a comment\n");
        Assert.That(result.FullTokens.ToArray().Select(t => t.Kind),
            Does.Contain(MoiraiTokenKind.Space).And.Contain(MoiraiTokenKind.Comment));
        Assert.That(result.ParseTokens.ToArray().Select(t => t.Kind),
            Has.None.EqualTo(MoiraiTokenKind.Space).And.None.EqualTo(MoiraiTokenKind.Comment));
        // LINE_BREAK is grammar-significant, not trivia — must survive filtering.
        Assert.That(result.ParseTokens.ToArray().Select(t => t.Kind),
            Does.Contain(MoiraiTokenKind.LineBreak));
    }

    [Test]
    public void ParseIndexToFullIndex_MapsBackCorrectly()
    {
        var result = MoiraiTokenizer.Tokenize("event e {} // c\n");
        var full = result.FullTokens.ToArray();
        var parse = result.ParseTokens.ToArray();
        for (int i = 0; i < parse.Length; i++)
            Assert.That(full[result.ParseIndexToFullIndex[i]].Kind, Is.EqualTo(parse[i].Kind));
    }
}
