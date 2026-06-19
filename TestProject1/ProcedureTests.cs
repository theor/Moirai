using Moirai.Parser;

namespace TestProject1;

// `function` doubles as the procedural keyword: a no-return function is a subroutine of effects,
// invoked via call(name) / call(name, count) or directly as name().
public class ProcedureTests : TestsBase
{
    private const string Story = @"
entity Country {
    prop founded: number
}
function make_country() {
    create Country $c: 'c' {
        founded := 1
    }
    record('a country is made')
}
@start
event setup {
    create Time $t: 'time' {
        year := 0
    }
    call(make_country, 3)
}";

    [Test]
    public void CallFunctionNTimes()
    {
        var db = Run(Story, out _, 0);   // parses (void function ok) + round-trips
        // @start runs setup -> call(make_country, 3)
        var made = db.Records.Count(r => r.Text == "a country is made");
        Assert.That(made, Is.EqualTo(3), "call(make_country, 3) runs the procedure three times");
        Assert.That(db.Entities.Count(e => db.GetEntityTypeName(e.Type) == "Country"), Is.EqualTo(3));
    }

    [Test]
    public void DirectProcedureCall()
    {
        const string s = @"
entity Country {
    prop founded: number
}
function make_country() {
    create Country $c: 'c'
    record('made')
}
event run {
    make_country()
    make_country()
}";
        var db = Run(s, out _, 0);
        db.RunAction(db.Actions.Single(a => a.Name == "run"));
        Assert.That(db.Records.Count(r => r.Text == "made"), Is.EqualTo(2));
    }
}
