using Moirai.Parser;

namespace Moirai.Tests;

/// A trigger's head is written as a query's: `when T $v: (predicate...)` for a change, `when created T $v`
/// for a creation. The name takes the slot `$new` had, so gating and the engine see the same trigger.
public class TriggerHeadTests : TestsBase
{
    private const string Prelude = "entity Person {\n    prop alive: bool\n    prop age: number\n}\n";

    static string[] Texts(Database db) =>
        db.Records.Select(r => System.Text.RegularExpressions.Regex.Replace(r.Text, @"<#\d+>(.*?)</>", "$1")).ToArray();

    [Test]
    public void TheNamedEntityIsTheOneThatChangedOrWasCreated()
    {
        var db = Run(Prelude + @"trigger born {
    when created Person $b: (age < 1)
    record('{$b.name} is born')
}
trigger dies {
    when Person $d: (alive = false, $old.alive)
    record('{$d.name} dies at {$d.age}')
}
event e {
    create Person $a: 'a' {
        age := 0
        alive := true
    }
    create Person $o: 'o' {
        age := 50
        alive := true
    }
}
event kill {
    pick Person $o: (age > 1)
    set $o.alive = false
}", out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "e"));
        // A change in the changeset that created the entity is part of its creation, so it dies in another.
        db.RunAction(db.Actions.Single(a => a.Name == "kill"));
        Assert.That(Texts(db), Is.EqualTo(new[] { "a is born", "o dies at 50" }));
    }

    [TestCase("when Person and alive = false and $old.alive", "write when Person $new: (alive = false and $old.alive)")]
    [TestCase("when Person", "write when Person $new")]
    [TestCase("when_created Person", "write when created Person $new")]
    [TestCase("when_created Person and age < 1", "write when created Person $new: (age < 1)")]
    public void TheOldHeadsAreErrorsThatSayWhatToWrite(string head, string fix)
    {
        StoryParser.Parse(Prelude + "trigger t {\n    " + head + "\n    record('x')\n}\n", out var errors);
        Assert.That(errors.Select(e => e.Message), Has.Some.Contains(fix), () => string.Join("\n", errors));
    }

    [Test]
    public void TheEntityCannotBeCalledOld()
    {
        StoryParser.Parse(Prelude + "trigger t {\n    when Person $old: (alive)\n    record('x')\n}\n", out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(StoryParser.ErrorCode.DuplicateVariableDefinition));
    }

    [Test]
    public void ACreatedTriggerHasNoOld()
    {
        StoryParser.Parse(Prelude + "trigger t {\n    when created Person $p: ($old.alive)\n    record('x')\n}\n", out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(StoryParser.ErrorCode.VariableNotDeclared));
    }
}
