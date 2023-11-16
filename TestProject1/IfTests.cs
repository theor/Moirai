namespace TestProject1;

public class IfTests : TestsBase
{
    [Test]
    public void IfTrue()
    {
        var db = Run(@"
entity T {}
prop p: number
rule r {
    create $t: T
    if true {
        set $t.p = 1
    }
}", out _);
        db.RunAction("r");
        Assert.AreEqual(1, db.Entities.Single().GetProperty(db.GetPropertyId("T","p")).IntValue);
    }

    [Test]
    public void IfFalse()
    {
        var db = Run(@"
entity T {}
prop p: number
rule r {
    create $t: T
    if false {
        set $t.p = 1
    } else {
        set $t.p = 2
    }
}", out _);
        db.RunAction("r");
        Assert.AreEqual(2, db.Entities.Single().GetProperty(db.GetPropertyId("T","p")).IntValue);
    }
    
    [Test]
    public void IfElseScopes()
    {
        var db = Run(@"
entity T {}
entity U {}
prop p: number
rule r {
    if false {
        create T
    } else {
        create U
    }
    set p = 2
}", out _);
        db.RunAction("r");
        Assert.AreEqual(2, db.Entities.Single().GetProperty(db.GetPropertyId("T","p")).IntValue);
    }
}
