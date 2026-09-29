namespace Moirai.Tests;

/// A function's arguments go in a frame at the top of the value stack. That frame was placed right only
/// when the caller's own frame started at 0, so a function called from inside another function, or from
/// an event that another rule called, read its arguments from the wrong slots and answered false.
public class FunctionFrameTests : TestsBase
{
    static string[] Texts(Database db) =>
        db.Records.Select(r => System.Text.RegularExpressions.Regex.Replace(r.Text, @"<#\d+>(.*?)</>", "$1")).ToArray();

    [Test]
    public void AFunctionCalledFromAFunctionReadsItsArguments()
    {
        const string s = @"
entity Person {
    prop age: number
}
function older_than($p: Person, $n: number): bool {
    $p.age > $n
}
function old($p: Person): bool {
    older_than($p, 60)
}
event e {
    create Person $a: 'a' {
        age := 70
    }
    create Person $b: 'b' {
        age := 10
    }
    each Person $p: (old($p)) {
        record('{$p.name} is old')
    }
    if old($a) {
        record('a, directly')
    }
}";
        var db = Run(s, out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "e"));
        Assert.That(Texts(db), Is.EqualTo(new[] { "a is old", "a, directly" }));
    }

    [Test]
    public void AFunctionInsideACalledEventReadsItsArguments()
    {
        const string s = @"
entity Person {
    prop age: number
}
function older_than($p: Person, $n: number): bool {
    $p.age > $n
}
event judge($p: Person) {
    var $limit: 60
    if older_than($p, $limit) {
        record('{$p.name} is old')
    }
}
event e {
    var $pad: 1
    create Person $a: 'a' {
        age := 70
    }
    judge($a)
}";
        var db = Run(s, out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "e"));
        Assert.That(Texts(db), Is.EqualTo(new[] { "a is old" }));
    }
}
