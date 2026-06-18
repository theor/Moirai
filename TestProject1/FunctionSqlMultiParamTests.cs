using Moirai.Parser;

namespace TestProject1;

public class FunctionSqlMultiParamTests : TestsBase
{
    // A two-argument bool helper used inside a SQL each predicate: the first arg is the query row,
    // the second a runtime ref. Previously threw IndexOutOfRange; now inlined into the caller scope.
    [Test]
    public void TwoParamBoolHelperInQuery()
    {
        var s = @"
entity Person {
    prop alive: bool
    prop p1: Person
    prop p2: Person
}
function is_child_of($ch: Person, $parent: Person): bool {
    $ch.p1 = $parent or $ch.p2 = $parent
}
event setup {
    create Person $a: 'a'
    create Person $b: 'b'
    create Person $c: 'c'
    set $c.alive = true
    set $c.p1 = $a
    set $b.alive = true
    set $b.p2 = $a
    each Person $ch: (is_child_of($ch, $a) and alive = true) {
        record('child of a: {$ch.name}')
    }
}";
        var db = Run(s, out _, 0);
        db.RunAction(db.Actions[0]);
        var children = db.Records.Count(r => r.Text.StartsWith("child of a:"));
        Assert.That(children, Is.EqualTo(2), "is_child_of() must match both children ($c via p1, $b via p2)");
    }

    // Same helper used in a pick predicate, mixing the runtime arg into the EXISTS-free path.
    [Test]
    public void TwoParamBoolHelperInPick()
    {
        var s = @"
entity Person {
    prop alive: bool
    prop p1: Person
    prop p2: Person
}
function is_child_of($ch: Person, $parent: Person): bool {
    $ch.p1 = $parent or $ch.p2 = $parent
}
event setup {
    create Person $a: 'a'
    create Person $c: 'c'
    set $c.alive = true
    set $c.p1 = $a
    if pick Person $ch: (is_child_of($ch, $a) and alive = true) {
        record('found a child')
    } else {
        record('none')
    }
}";
        var db = Run(s, out _, 0);
        db.RunAction(db.Actions[0]);
        Assert.That(db.Records.Select(r => r.Text), Does.Contain("found a child"));
    }
}
