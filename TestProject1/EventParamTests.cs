using Moirai.Parser;

namespace TestProject1;

// Parameterized events: `event greet($name: string, $j: Job) { ... }` invoked via
// call(greet, 'Alice', Job.Smith). Arguments bind to the event scope's first slots.
public class EventParamTests : TestsBase
{
    private const string Story = @"
enum Job { Farmer, Smith }
event greet($name: string, $j: Job) {
    record('{$name} is a {$j}')
}
event setup {
    call(greet, 'Alice', Job.Smith)
    call(greet, 'Bob', Job.Farmer)
    var $who: 'Carol'
    call(greet, $who, Job.Farmer)
}";

    [Test]
    public void ArgumentsBindInOrderAndRoundTrip()
    {
        var db = Run(Story, out _, 0);
        var setup = db.Actions.Single(a => a.Name == "setup");
        db.RunAction(setup);

        var texts = db.Records.Select(r => r.Text).ToList();
        Assert.That(texts, Does.Contain("Alice is a Smith"));
        Assert.That(texts, Does.Contain("Bob is a Farmer"));
        // Argument computed from a caller-frame local ($who) before the callee frame opens.
        Assert.That(texts, Does.Contain("Carol is a Farmer"));
    }

    [Test]
    public void ParametersDeclaredOnEventTrigger()
    {
        var db = Run(Story, out _, 0);
        var greet = db.Actions.Single(a => a.Name == "greet");
        Assert.That(greet.Parameters, Is.Not.Null);
        Assert.That(greet.Parameters!.Count, Is.EqualTo(2));
        Assert.That(greet.Parameters[0].ParamName, Is.EqualTo("$name"));
        Assert.That(greet.Parameters[1].ParamName, Is.EqualTo("$j"));
    }

    [Test]
    public void ArgsAreIsolatedFromCallerLocals()
    {
        // The caller declares its own locals before/after the call; the callee's parameter slots
        // must not clobber them.
        const string s = @"
event callee($x: number) {
    record('callee {$x}')
}
event caller {
    var $a: 'before'
    call(callee, 7)
    record('caller {$a}')
}";
        var db = Run(s, out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "caller"));
        var texts = db.Records.Select(r => r.Text).ToList();
        Assert.That(texts, Does.Contain("callee 7"));
        Assert.That(texts, Does.Contain("caller before"),
            "caller's local survives the parameterized call");
    }

    [Test]
    public void TypeMismatchIsAnError()
    {
        const string s = @"
enum Job { Farmer, Smith }
event greet($name: string, $j: Job) {
    record('{$name}')
}
event setup {
    call(greet, Job.Smith, 'oops')
}";
        StoryParser.Parse(s, out var errors);
        Assert.That(errors.Any(e => e.Code == StoryParser.ErrorCode.MismatchedAssignmentTypes));
    }

    [Test]
    public void MissingArgumentIsAnError()
    {
        const string s = @"
event greet($name: string, $other: string) {
    record('{$name}')
}
event setup {
    call(greet, 'only one')
}";
        StoryParser.Parse(s, out var errors);
        Assert.That(errors.Any(e => e.Code == StoryParser.ErrorCode.MissingArgument));
    }
}
