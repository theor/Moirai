using Moirai.Parser;

namespace Moirai.Tests;

/// Every function is declared before any body is parsed, as events are, so the order a story writes its
/// functions in means nothing: a function can call one below it, itself, and a method can call a
/// top-level function.
public class FunctionOrderTests : TestsBase
{
    static string[] Texts(Database db) =>
        db.Records.Select(r => System.Text.RegularExpressions.Regex.Replace(r.Text, @"<#\d+>(.*?)</>", "$1")).ToArray();

    [Test]
    public void AFunctionCanCallOneWrittenBelowIt()
    {
        const string s = @"
entity Person {
    prop age: number
}
function old($p: Person): bool {
    older_than($p, 60)
}
function older_than($p: Person, $n: number): bool {
    $p.age > $n
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
}";
        var db = Run(s, out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "e"));
        Assert.That(Texts(db), Is.EqualTo(new[] { "a is old" }));
    }

    [Test]
    public void AMethodCanCallATopLevelFunction()
    {
        const string s = @"
entity Person {
    prop age: number
    function is_old(): bool {
        over_sixty($self.age)
    }
}
function over_sixty($n: number): bool {
    $n > 60
}
event e {
    create Person $a: 'a' {
        age := 70
    }
    if $a.is_old() {
        record('old')
    }
}";
        var db = Run(s, out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "e"));
        Assert.That(Texts(db), Is.EqualTo(new[] { "old" }));
    }

    [Test]
    public void AProcedureCanCallItself()
    {
        const string s = @"
function countdown($n: number) {
    if $n > 0 {
        record('{$n}')
        countdown($n - 1)
    }
}
event e {
    countdown(3)
}";
        var db = Run(s, out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "e"));
        Assert.That(Texts(db), Is.EqualTo(new[] { "3", "2", "1" }));
    }

    /// A pick reads through a one-expression function to narrow its candidates. A function that calls
    /// itself is read once and then left to the full predicate, so the narrowing ends and finds the same
    /// matches a scan would.
    [Test]
    public void ARecursivePredicateNarrowsAndFindsWhatAScanWould()
    {
        const string s = @"
entity Person {
    prop parent1: Person
}
function descends($c: Person, $a: Person): bool {
    $c.parent1 = $a or ($c.parent1 != null and descends($c.parent1, $a))
}
event e {
    create Person $root: 'root'
    create Person $child: 'child' {
        parent1 := $root
    }
    create Person $grandchild: 'grandchild' {
        parent1 := $child
    }
    create Person $stranger: 'stranger'
    each Person $p: (descends($p, $root)) {
        record('{$p.name}')
    }
}";
        var db = Run(s, out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "e"));
        Assert.That(Texts(db), Is.EqualTo(new[] { "child", "grandchild" }));
    }

    [Test]
    public void ADisplayCanUseATopLevelFunction()
    {
        const string s = @"
@display(Person, 'Children', is_child_of($other, $self))
entity Person {
    prop parent1: Person
}
function is_child_of($c: Person, $p: Person): bool {
    $c.parent1 = $p
}";
        Run(s, out _, 0);
    }

    [Test]
    public void TwoFunctionsWithOneNameAreAnError()
    {
        StoryParser.Parse("function f(): number {\n    1\n}\nfunction f(): number {\n    2\n}\n", out var errors);
        Assert.That(errors.Select(e => e.Code), Has.Some.EqualTo(StoryParser.ErrorCode.DuplicateDefinition));
    }
}
