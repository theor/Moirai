using Moirai.Parser;

namespace Moirai.Tests;

/// A local is given its value with `=`, as a property is by `set`. The old `var $x: e` is still read,
/// only so the error can say what to write.
public class LocalVarTests : TestsBase
{
    [Test]
    public void TheColonFormIsAnErrorThatSaysWhatToWrite()
    {
        StoryParser.Parse("event e {\n    var $x: 3\n}\n", out var errors);
        Assert.That(errors.Select(e => e.Message), Has.Some.Contains("write var $x = ..."));
    }

    [Test]
    public void AnEqualityCanBeTheValue()
    {
        var db = Run("event e {\n    var $same = 1 = 1\n    if $same {\n        record('same')\n    }\n}\n", out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "e"));
        Assert.That(db.Records.Select(r => r.Text), Is.EqualTo(new[] { "same" }));
    }
}
