using Moirai.Parser;
using Moirai.Parser.Ast;

namespace TestProject1;

/// <summary>
/// The parser flags user-function calls that are inlined into a SQL query (i.e. calls inside a
/// pick/each predicate) with an Information-severity <see cref="StoryParser.ErrorCode.FunctionInlinedToSql"/>
/// marker, so the editor can show that those calls aren't step-debuggable. Procedural calls are not flagged.
/// </summary>
public class InlinedFunctionMarkerTests
{
    private const string Story = @"
entity Person {
    prop age: number
}
function adult($p: Person): bool {
    $p.age >= 18
}
function greet() {
    record('hi')
}
event run {
    each Person $p: (adult($p)) {
        greet()
    }
}";

    private static int LineOf(string text, string needle)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains(needle)) return i + 1;
        return -1;
    }

    // Parse and expose both the parse errors and the (separate) info markers.
    private static (List<StoryParser.Error> errors, List<StoryParser.Error> markers) Parse(string s)
    {
        var db = new Database();
        var visitor = new AstVisitor(db);
        var tokenized = MoiraiTokenizer.Tokenize(s);
        var parsed = MoiraiGrammar.TryParseR(tokenized.ParseTokens);
        Assert.That(parsed.HasValue, Is.True, () => parsed.ToString());
        visitor.VisitR(parsed.Value);
        return (visitor.Errors, visitor.InfoMarkers);
    }

    [Test]
    public void FlagsPredicateCallButNotProceduralCall()
    {
        var (errors, markers) = Parse(Story);

        // No hard errors, and markers do NOT pollute the parse-error list.
        Assert.That(errors.Count(e => e.Severity == StoryParser.Severity.Error), Is.Zero,
            string.Join("\n", errors));
        Assert.That(errors.Any(e => e.Code == StoryParser.ErrorCode.FunctionInlinedToSql), Is.False,
            "markers must not appear in the parse-error list");

        var inlined = markers.Where(e => e.Code == StoryParser.ErrorCode.FunctionInlinedToSql).ToList();

        // Exactly one marker: adult($p) in the each predicate. greet() (procedural) is not flagged.
        Assert.That(inlined, Has.Count.EqualTo(1), "expected exactly one inlined-call marker");
        Assert.That(inlined[0].Severity, Is.EqualTo(StoryParser.Severity.Information));

        int predicateLine = LineOf(Story, "each Person $p: (adult($p))");
        Assert.That(inlined[0].Line, Is.EqualTo(predicateLine), "marker should sit on the predicate call site");
    }

    [Test]
    public void WhenPredicateCallsAreNotFlagged()
    {
        // `when` predicates are evaluated in-memory (Compute), not compiled to SQL, so calls there
        // remain steppable and must not be flagged.
        const string s = @"
entity Person {
    prop age: number
    prop adult: bool
}
function isAdult($p: Person): bool {
    $p.age >= 18
}
trigger mark_adult {
    when Person and isAdult($new)
    set $new.adult = true
}";
        var (_, markers) = Parse(s);
        Assert.That(markers.Any(e => e.Code == StoryParser.ErrorCode.FunctionInlinedToSql), Is.False,
            "when-predicate calls must not be flagged as SQL-inlined");
    }
}
