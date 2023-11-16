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
        Assert.AreEqual(1, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
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
        Assert.AreEqual(2, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }
    
    [Test]
    public void VarDecl_IfElseScopes_Fails()
    {
        var db = Run(@"
entity T {}
entity U {}
prop p: number
rule r {
    if false {
        create $x: T
    } else {
        create $x: U
    }
    set $x.p = 2
}", out _, 1);
    }
    [Test]
    public void IfElseScopes()
    {
        var db = Run(@"
entity T {}
entity U {}
prop p: number
rule r {
    var $x: if false {
        create $x: T
    } else {
        create $x: U
    }
    set $x.p = 2
}", out _);
        db.RunAction("r");
        Assert.AreEqual(2, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }
}
