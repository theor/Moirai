using Moirai.Parser;

namespace Moirai.Tests;

/// `if (pick T $v: (...)) { }` binds $v only for the then-block: after the if, or in its else, the pick
/// may have missed, and $v used to stay visible there and read an empty slot.
public class IfPickScopeTests : TestsBase
{
    private const string Prelude = "entity Person {\n    prop age: number\n}\n";

    [TestCase("if (pick Person $p: (age > 1)) {\n        record('in')\n    }\n    record('{$p.name} after')")]
    [TestCase("if (pick Person $p: (age > 1)) {\n        record('in')\n    } else {\n        record('{$p.name}')\n    }")]
    public void ThePickedVariableExistsOnlyInTheThenBlock(string body)
    {
        StoryParser.Parse(Prelude + "event e {\n    " + body + "\n}\n", out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(StoryParser.ErrorCode.VariableNotDeclared),
            () => string.Join("\n", errors));
    }

    [Test]
    public void TheThenBlockSeesIt()
    {
        var db = Run(Prelude + @"event e {
    create Person $a: 'a' {
        age := 3
    }
    if (pick Person $p: (age > 1)) {
        record('{$p.age}')
    }
    if (pick Person $p: (age > 1)) {
        record('again {$p.age}')
    }
}", out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "e"));
        Assert.That(db.Records.Select(r => System.Text.RegularExpressions.Regex.Replace(r.Text, @"<#\d+>(.*?)</>", "$1")),
            Is.EqualTo(new[] { "3", "again 3" }));
    }
}
