using Moirai.Parser;

namespace Moirai.Tests;

/// A schedule block runs in a later year, as a rule of its own, when the locals of the rule that scheduled
/// it are gone: it sees `$self` and nothing else. Naming one of those locals used to parse and read an empty
/// slot at run time; it is now an error.
public class ScheduleScopeTests
{
    const string Types = "entity P {\n    prop n: number\n    prop alive: bool\n}\n";

    static List<StoryParser.Error> Errors(string story)
    {
        StoryParser.Parse(Types + story, out var errors);
        return errors.Where(e => e.Severity == StoryParser.Severity.Error).ToList();
    }

    [TestCase("event e {\n    pick P $p\n    var $k: 5\n    schedule($p, 10) {\n        set $self.n = $k\n    }\n}\n", "$k")]
    [TestCase("event e {\n    pick P $p\n    schedule($p, 10) {\n        set $p.n = 1\n    }\n}\n", "$p")]
    [TestCase("trigger t {\n    when_created P\n    schedule($new, 10) {\n        set $new.n = 1\n    }\n}\n", "$new")]
    [TestCase("event e($who: P) {\n    schedule($who, 10) {\n        set $who.n = 1\n    }\n}\n", "$who")]
    public void ARuleLocalInsideAScheduleBlockIsAnError(string story, string local)
    {
        var errors = Errors(story);
        Assert.That(errors, Has.Some.Matches<StoryParser.Error>(e =>
                e.Code == StoryParser.ErrorCode.VariableNotDeclared && e.Message.Contains(local) && e.Message.Contains("$self")),
            () => string.Join("\n", errors));
    }

    /// The outer block's `$self` is the outer rule's, as far as the inner block is concerned.
    [Test]
    public void AnOuterSelfInsideANestedScheduleIsAnError()
    {
        var errors = Errors("event e {\n    pick P $p\n    schedule($p, 10) {\n        var $outer: $self\n" +
                            "        schedule($self, 20) {\n            set $self.n = 1\n            var $x: $outer\n        }\n    }\n}\n");
        Assert.That(errors.Select(e => e.Message), Has.Some.Contains("$outer"));
        Assert.That(errors, Has.Count.EqualTo(1), () => string.Join("\n", errors));
    }

    /// What the block may use: `$self`, its own locals, singletons, and -- in the arguments, which are read
    /// when the schedule is made -- anything the rule has.
    [Test]
    public void SelfItsOwnLocalsSingletonsAndTheArgumentsAreFine()
    {
        var errors = Errors("event e {\n    pick P $p\n    var $delay: 5\n    schedule($p, #Time.year + $delay) {\n" +
                            "        var $n: $self.n + 1\n        set $self.n = $n\n        if $self.alive {\n            set $self.n = #Time.year\n        }\n    }\n}\n");
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors));
    }
}
