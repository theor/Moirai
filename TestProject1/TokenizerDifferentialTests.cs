extern alias MoiraiAntlr;
using Antlr4.Runtime;
using Moirai.Parser;
using AntlrLexer = MoiraiAntlr::Moirai.Parser.moirai_lexer;

namespace TestProject1;

/// Phase 1 gate of the ANTLR->Superpower migration (see
/// C:\Users\theor\.claude\plans\stateful-dancing-stroustrup.md): proves MoiraiTokenizer is
/// token-for-token identical to the ANTLR lexer (kind, text, line, column) before any grammar work
/// starts. Compares against the frozen Moirai.Parser.Antlr snapshot via `extern alias`, since both
/// projects share the `Moirai.Parser` namespace and can't be referenced unaliased side by side.
public class TokenizerDifferentialTests : TestsBase
{
    static readonly Dictionary<string, MoiraiTokenKind> AntlrNameToKind = new()
    {
        ["QUOTE"] = MoiraiTokenKind.Quote,
        ["NULL"] = MoiraiTokenKind.Null,
        ["SPACE"] = MoiraiTokenKind.Space,
        ["LINE_BREAK"] = MoiraiTokenKind.LineBreak,
        ["COMMENT"] = MoiraiTokenKind.Comment,
        ["COLON_EQ"] = MoiraiTokenKind.ColonEq,
        ["COLON"] = MoiraiTokenKind.Colon,
        ["SCOPE_OPEN"] = MoiraiTokenKind.ScopeOpen,
        ["SCOPE_CLOSE"] = MoiraiTokenKind.ScopeClose,
        ["EXPR_OPEN"] = MoiraiTokenKind.ExprOpen,
        ["PAREN_OPEN"] = MoiraiTokenKind.ParenOpen,
        ["PAREN_CLOSE"] = MoiraiTokenKind.ParenClose,
        ["LBRACK"] = MoiraiTokenKind.LBrack,
        ["RBRACK"] = MoiraiTokenKind.RBrack,
        ["EVENT"] = MoiraiTokenKind.Event,
        ["ENTITY"] = MoiraiTokenKind.Entity,
        ["SINGLETON"] = MoiraiTokenKind.Singleton,
        ["TRIGGER"] = MoiraiTokenKind.Trigger,
        ["PROP"] = MoiraiTokenKind.Prop,
        ["FUNCTION"] = MoiraiTokenKind.Function,
        ["ENUM"] = MoiraiTokenKind.Enum,
        ["TABLE"] = MoiraiTokenKind.Table,
        ["WHEN"] = MoiraiTokenKind.When,
        ["WHEN_CREATED"] = MoiraiTokenKind.WhenCreated,
        ["SET"] = MoiraiTokenKind.Set,
        ["VAR"] = MoiraiTokenKind.Var,
        ["MATCH"] = MoiraiTokenKind.Match,
        ["MATCH_WEIGHT"] = MoiraiTokenKind.MatchWeight,
        ["COMMA"] = MoiraiTokenKind.Comma,
        ["ARROW"] = MoiraiTokenKind.Arrow,
        ["IF"] = MoiraiTokenKind.If,
        ["ELSE"] = MoiraiTokenKind.Else,
        ["TRUE"] = MoiraiTokenKind.True,
        ["FALSE"] = MoiraiTokenKind.False,
        ["DOT"] = MoiraiTokenKind.Dot,
        ["NEQ"] = MoiraiTokenKind.Neq,
        ["EQ"] = MoiraiTokenKind.Eq,
        ["QQ"] = MoiraiTokenKind.Qq,
        ["ADD"] = MoiraiTokenKind.Add,
        ["SUB"] = MoiraiTokenKind.Sub,
        ["MUL"] = MoiraiTokenKind.Mul,
        ["DIV"] = MoiraiTokenKind.Div,
        ["MOD"] = MoiraiTokenKind.Mod,
        ["GE"] = MoiraiTokenKind.Ge,
        ["LE"] = MoiraiTokenKind.Le,
        ["GT"] = MoiraiTokenKind.Gt,
        ["LT"] = MoiraiTokenKind.Lt,
        ["AND"] = MoiraiTokenKind.And,
        ["OR"] = MoiraiTokenKind.Or,
        ["SINGLETON_ID"] = MoiraiTokenKind.SingletonId,
        ["VAR_ID"] = MoiraiTokenKind.VarId,
        ["PROP_ID"] = MoiraiTokenKind.PropId,
        ["AT"] = MoiraiTokenKind.At,
        ["TYPE_ID"] = MoiraiTokenKind.TypeId,
        ["ID"] = MoiraiTokenKind.Id,
        ["PERCENT"] = MoiraiTokenKind.Percent,
        ["NUMBER_FLOAT"] = MoiraiTokenKind.NumberFloat,
        ["NUMBER"] = MoiraiTokenKind.Number,
        ["TEXT"] = MoiraiTokenKind.Text,
    };

    static IEnumerable<string> CorpusFiles()
    {
        yield return "../../../../MoiraiCli/w.sg";
        // MoiraiCli/test.sg deliberately excluded: it contains a double-quoted string literal
        // ("Solar System") the grammar has never supported (only ' is a valid string delimiter;
        // there's no DOUBLE_QUOTE lexer rule) -- confirmed independently here, since the ANTLR
        // lexer itself reports "token recognition error at: '\"'" on it. Stale/dead content, not
        // a real DSL fixture; not exercised by ParsingTests.GetFilePaths either.
        yield return "../../../../MoiraiCli/space.sg";
        yield return "../../../../MoiraiWebServer/wwwroot/space.sg";
    }

    static void AssertTokenStreamsMatch(string content)
    {
        var lexer = new AntlrLexer(new CodePointCharStream(content));
        var antlrTokens = lexer.GetAllTokens()
            .Where(t => t.Type != TokenConstants.EOF)
            .ToList();

        var result = MoiraiTokenizer.Tokenize(content);
        var ours = result.FullTokens.ToArray();

        Assert.That(result.Errors, Is.Empty,
            "Tokenizer reported errors on well-formed corpus input: " +
            string.Join("; ", result.Errors.Select(e => e.Message)));

        Assert.That(ours.Length, Is.EqualTo(antlrTokens.Count),
            $"Token count mismatch: ANTLR={antlrTokens.Count}, ours={ours.Length}. " +
            DescribeFirstDivergence(antlrTokens, ours));

        for (int i = 0; i < antlrTokens.Count; i++)
        {
            var a = antlrTokens[i];
            var o = ours[i];
            var expectedKind = AntlrNameToKind[lexer.Vocabulary.GetSymbolicName(a.Type)];

            Assert.That(o.Kind, Is.EqualTo(expectedKind),
                $"Token #{i}: kind mismatch (ANTLR '{a.Text}' @ {a.Line}:{a.Column})");
            Assert.That(o.ToStringValue(), Is.EqualTo(a.Text),
                $"Token #{i} ({o.Kind}): text mismatch");
            // ANTLR: 1-based line, 0-based column. Superpower Position: 1-based line, 1-based column.
            Assert.That(o.Position.Line, Is.EqualTo(a.Line),
                $"Token #{i} ({o.Kind}, '{a.Text}'): line mismatch");
            Assert.That(o.Position.Column - 1, Is.EqualTo(a.Column),
                $"Token #{i} ({o.Kind}, '{a.Text}'): column mismatch");
        }
    }

    static string DescribeFirstDivergence(IReadOnlyList<IToken> antlrTokens,
        IReadOnlyList<Superpower.Model.Token<MoiraiTokenKind>> ours)
    {
        int n = Math.Min(antlrTokens.Count, ours.Count);
        for (int i = 0; i < n; i++)
        {
            var a = antlrTokens[i];
            var o = ours[i];
            if (o.ToStringValue() != a.Text)
                return $"First divergence at #{i}: ANTLR '{a.Text}' @ {a.Line}:{a.Column} vs ours '{o.ToStringValue()}' ({o.Kind})";
        }

        return "No content divergence found before the shorter stream ended.";
    }

    [Test]
    [TestCaseSource(nameof(CorpusFiles))]
    public void CorpusFile_TokenStreamMatchesAntlrLexer(string rpath)
    {
        var path = Path.GetFullPath(rpath);
        if (!File.Exists(path))
            Assert.Inconclusive(path);
        AssertTokenStreamsMatch(File.ReadAllText(path));
    }

    [TestCase("event e { record '{$a} and {$b}' }")]
    [TestCase("event e { record '{random(Name)} of {$c.name}' }")]
    // Re-entrant string-inside-interpolation: a string literal nested inside a `{expr}` inside a
    // string. Grammar-legal (arbitrary EXPR_OPEN/QUOTE mode-stack recursion) even if unused in
    // MoiraiCli's sample stories.
    [TestCase("event e { record '{func('nested {$x}')}' }")]
    [TestCase("event e { record 'a\\'b' }")] // escaped quote inside a string
    [TestCase("event e { record '' }")] // empty string (zero-length TEXT run — no TEXT token at all)
    [TestCase("event e { create Type $t: 'x' { p := 1 } }")] // ordinary '{'/'}' (type-body-shaped), not interpolation
    public void Fixture_TokenStreamMatchesAntlrLexer(string content) => AssertTokenStreamsMatch(content);

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
    public void NumberFixture_TokenStreamMatchesAntlrLexer(string expr) =>
        AssertTokenStreamsMatch($"event e {{ var $r: {expr} }}");

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
