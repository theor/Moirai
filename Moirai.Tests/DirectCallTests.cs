using Moirai.Parser;

namespace Moirai.Tests;

/// Events and procedures are called by name, `harvest()` and `greet($p)`, and `repeat(n) { ... }` runs a
/// block n times. Together they replace `call(event, args...)` and `call(event, n)`.
public class DirectCallTests
{
    const string Story = @"
entity Person {
    prop name_seen: bool
}
function feast() {
    record('a feast')
}
event greet($who: Person) {
    record('{$who.name} is greeted')
}
event harvest {
    record('a harvest of {random(1, 100)}')
}
event lonely {
    pick Person $p: (name_seen)
    record('found {$p.name}')
}
";

    static Database Run(string body, out List<StoryParser.Error> errors)
    {
        var db = StoryParser.Parse(Story + "event main {\n" + body + "\n}\n", out errors);
        db.History = new();
        db.Init();
        return db;
    }

    static List<string> Records(string body)
    {
        var db = Run(body, out var errors);
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors));
        db.RunAction("main");
        return db.Records.Select(r => r.Text).ToList();
    }

    [Test]
    public void AnEventOrAProcedureIsCalledByName()
    {
        var records = Records("    create Person $p: 'Ada'\n    greet($p)\n    feast()\n    harvest()");
        Assert.That(records[0], Does.Contain("Ada").And.EndWith("is greeted"));
        Assert.That(records[1], Is.EqualTo("a feast"));
        Assert.That(records[2], Does.StartWith("a harvest of"));
    }

    [TestCase("    greet()", StoryParser.ErrorCode.MissingArgument)]
    [TestCase("    greet(3)", StoryParser.ErrorCode.MismatchedAssignmentTypes)]
    [TestCase("    harvest(3)", StoryParser.ErrorCode.MissingArgument)]
    [TestCase("    harvest() {\n        record('x')\n    }", StoryParser.ErrorCode.InvalidArgument)]
    public void ADirectCallIsCheckedAgainstTheEventsParameters(string body, StoryParser.ErrorCode code)
    {
        Run(body, out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(code), () => string.Join("\n", errors));
    }

    [Test]
    public void RepeatRunsItsBlockNTimesAndAStopEndsOnlyThatTurn()
    {
        // lonely's pick finds nobody, so lonely fails -- and, as after a failed pick of its own, the caller
        // stops there: each turn records, then ends at the call. Every turn still runs.
        var records = Records("    repeat(3) {\n        record('a turn')\n        lonely()\n        record('unreached')\n    }");
        Assert.That(records, Is.EqualTo(new[] { "a turn", "a turn", "a turn" }));

        // A stop in the block itself ends that turn only.
        records = Records("    repeat(2) {\n        pick Person $p: (name_seen)\n        record('unreached')\n    }\n    record('after')");
        Assert.That(records, Is.EqualTo(new[] { "after" }));
    }

    [Test]
    public void RepeatTakesAnyNumberExpression()
    {
        var records = Records("    create Person $a: 'A'\n    create Person $b: 'B'\n    repeat(count Person $p) {\n        feast()\n    }");
        Assert.That(records, Is.EqualTo(new[] { "a feast", "a feast" }));
    }

    /// Every rule draws from its own random stream and repeat draws nothing, so repeating a call is the same
    /// history as the old count form.
    [Test]
    public void RepeatingACallIsTheSameHistoryAsTheCountForm()
    {
        Assert.That(Records("    repeat(5) {\n        harvest()\n    }"), Is.EqualTo(Records("    call(harvest, 5)")));
    }

    [TestCase("event floor {\n    record('x')\n}")]
    [TestCase("event harvest2 {\n    record('x')\n}\nfunction harvest2() {\n    record('y')\n}")]
    public void EventsFunctionsAndBuiltinsShareOneNamespace(string definitions)
    {
        StoryParser.Parse(definitions + "\n", out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(StoryParser.ErrorCode.DuplicateDefinition),
            () => string.Join("\n", errors));
    }
}
