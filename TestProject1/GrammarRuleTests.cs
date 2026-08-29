using Moirai.Parser;
using Moirai.Parser.Ast;

namespace TestProject1;

/// Per-rule fixtures for the Superpower grammar (Phase 2 of the ANTLR->Superpower migration, see
/// C:\Users\theor\.claude\plans\stateful-dancing-stroustrup.md) — trailing commas, blank lines, the
/// single-trailing-effect-no-break case, and paren-less vs. parenthesized calls, as called out in
/// the plan's Phase 2 verification section. GrammarStructuralTests already proves the full corpus
/// parses; these target specific rule shapes in isolation so a future regression points at the
/// right rule directly instead of somewhere in a 1000-line file.
public class GrammarRuleTests : TestsBase
{
    static void AssertParses(string source)
    {
        var tokenized = MoiraiTokenizer.Tokenize(source);
        Assert.That(tokenized.Errors, Is.Empty);
        var result = MoiraiGrammar.TryParseR(tokenized.ParseTokens);
        Assert.That(result.HasValue, Is.True,
            () => $"{result.ErrorMessage} at {result.ErrorPosition}, expected: " +
                  string.Join(" / ", result.Expectations ?? Array.Empty<string>()));
        Assert.That(result.Remainder.IsAtEnd, Is.True, "did not consume the whole token stream");
    }

    [TestCase("enum Job {\n  Farmer,\n  Smith,\n}\n")] // trailing comma
    [TestCase("enum Job {\n  Farmer,\n  Smith\n}\n")] // no trailing comma
    [TestCase("enum Job {\n  Farmer\n}\n")] // single member
    public void EnumDefinition_TrailingCommaOptional(string source) => AssertParses(source);

    [TestCase("table T {\n  'a', 'b',\n}\n")]
    [TestCase("table T {\n  'a', 'b'\n}\n")]
    [TestCase("table T {\n  70 => 'a', 30 => 'b',\n}\n")] // weighted entries, trailing comma
    public void TableDefinition_TrailingCommaOptional(string source) => AssertParses(source);

    [Test]
    public void Scope_BlankLinesInsideAreLegal() => AssertParses(
        "trigger t {\n  when Person and alive = true\n\n\n  record('x')\n}\n");

    [Test]
    public void Scope_SingleTrailingEffect_NoLineBreakBeforeClose() => AssertParses(
        "event e { record('x') }\n");

    [Test]
    public void Scope_EmptyBody() => AssertParses("event e {}\n");

    [TestCase("event e { record('x') }\n")] // parenthesized call
    [TestCase("event e { record 'x' }\n")] // paren-less (raw_call) form
    public void Record_ParenAndParenLess_BothParse(string source) => AssertParses(source);

    [Test]
    public void Create_BareForm_NoNameNoInit() =>
        AssertParses("entity Person {\n  prop age: number\n}\nevent e { create Person $p }\n");

    [Test]
    public void Create_TypedDeclWithParenthesizedCallArgs() =>
        AssertParses("entity Object {}\nevent e { create Object $p: ('Mercury') }\n");

    [Test]
    public void Create_TypedDeclWithInitBlock() => AssertParses(
        "entity Time {\n  prop year: number\n}\nevent e { create Time $t: 'time' { year := 764 } }\n");

    [Test]
    public void If_WithElse() => AssertParses(
        "event e { if (true) { record('a') } else { record('b') } }\n");

    [Test]
    public void If_WithoutElse() => AssertParses("event e { if (true) { record('a') } }\n");

    [Test]
    public void Match_WithWildcardArm() => AssertParses(
        "event e { var $r: match true { true => 1 _ => 2 } }\n");

    [Test]
    public void RandomWeighted_AsStatement() => AssertParses(
        "event e { random_weighted 100 { 50 => record('a') 50 => record('b') } }\n");

    [Test]
    public void RandomWeighted_AsExpression() => AssertParses(
        "event e { var $d: random_weighted 100 { 50 => 'a' _ => 'b' } }\n");

    [Test]
    public void Trigger_WhenCreated() =>
        AssertParses("entity Item {}\ntrigger t { when_created Item\n  record('x')\n}\n");

    [Test]
    public void Trigger_WhenWithAndChain() => AssertParses(
        "entity Item {\n  prop a: bool\n  prop b: bool\n}\n" +
        "trigger t { when Item and a = true and b = false\n  record('x')\n}\n");

    [Test]
    public void FunctionDefinition_WithParamsAndReturnType() => AssertParses(
        "function f($x: number, $y: number): number {\n  $x + $y\n}\n" +
        "event e { var $r: call(f) }\n");

    [Test]
    public void Attributes_Stacked() => AssertParses(
        "@tag('a', 'b')\n@frequency(1, PerXYear, 15)\nevent e {}\n");

    [Test]
    public void NestedStringInterpolation() => AssertParses(
        "function func($s: string): string { $s }\n" +
        "event e { record('{func('nested text')}') }\n");

    [TestCase("2 + 3 * 4")]
    [TestCase("-4 - -3")]
    [TestCase("50% + 10")]
    [TestCase("(1 + 2) * 3")]
    [TestCase("$x.a.b.c")]
    [TestCase("#Time.year")]
    [TestCase("a and b or c")]
    [TestCase("a ?? b")]
    public void Expr_ParsesInIsolation(string expr)
    {
        var tokenized = MoiraiTokenizer.Tokenize(expr);
        Assert.That(tokenized.Errors, Is.Empty);
        var result = MoiraiGrammar.TryParseExpr(tokenized.ParseTokens);
        Assert.That(result.HasValue, Is.True,
            () => $"{result.ErrorMessage} at {result.ErrorPosition}");
        Assert.That(result.Remainder.IsAtEnd, Is.True);
    }

    [Test]
    public void Expr_OperatorPrecedence_MulBindsTighterThanAdd()
    {
        var tokenized = MoiraiTokenizer.Tokenize("2 + 3 * 4");
        var result = MoiraiGrammar.TryParseExpr(tokenized.ParseTokens);
        Assert.That(result.HasValue, Is.True);
        var expr = result.Value;
        Assert.That(expr.Op, Is.EqualTo("+")); // outermost node is the '+' (loosest of the two)
        Assert.That(expr.Right!.Op, Is.EqualTo("*"));
    }

    [Test]
    public void Set_ImplicitCurrentEntityShorthand() =>
        // space.sg's `set year = 0` pattern: `set` followed by a bare property_id (no $var.
        // prefix) -- a plain `path` whose root is PropertyId only.
        AssertParses("entity Object {\n  prop year: number\n}\n" +
                     "event e { create Object $p: 'x' { } set year = 0 }\n");
}
