using Moirai.Parser;

namespace Moirai.Tests;

/// Events and procedures are called by name, `harvest()` and `greet($p)`, and `repeat(n) { ... }` runs a
/// block n times. Together they replaced `call(event, args...)` and `call(event, n)`, which is gone.
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
        Assert.That(errors.Where(e => e.Severity == StoryParser.Severity.Error), Is.Empty, () => string.Join("\n", errors));
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
        // lonely's pick finds nobody, so lonely fails. A called event is a rule of its own, and so is its
        // failure: the caller carries on, and every turn runs to its end.
        var records = Records("    repeat(2) {\n        lonely()\n        record('a turn')\n    }");
        Assert.That(records, Is.EqualTo(new[] { "a turn", "a turn" }));

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
    /// history as writing it out -- and as the `call(harvest, 5)` it replaced.
    [Test]
    public void RepeatingACallIsTheSameHistoryAsWritingItOut()
    {
        Assert.That(Records("    repeat(5) {\n        harvest()\n    }"),
            Is.EqualTo(Records(string.Concat(Enumerable.Repeat("    harvest()\n", 5)))));
    }

    /// `call` is gone; a story that still uses it is told what to write instead.
    [Test]
    public void CallIsGoneAndSaysWhatToWriteInstead()
    {
        Run("    call(harvest)", out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(StoryParser.ErrorCode.UnknownInstruction));
        Assert.That(errors.Select(e => e.Message), Has.Some.Contains("repeat(n) { name() }"));
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
