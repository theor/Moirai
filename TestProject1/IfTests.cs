namespace TestProject1;

public class RandomTests : TestsBase
{
    [Test]
    public void RandomRange()
    {
        Run(@"
event c {
   var $x: random (1, 10)
    assert ($x >= 1)
    assert ($x <= 10)
}
@start
event r {
    call (c, 100)
}", out _);
    }
    [Test]
    public void RandomMax()
    {
        Run(@"
event c {
   var $x: random (10)
    assert ($x >= 0)
    assert ($x <= 10)
}
@start
event r {
    call (c, 100)
}", out _);
    }
}

public class CoalesceTests : TestsBase
{
    [Test]
    public void CoalesceNullInt()
    {
        Run(@"
@start
event e {
    var $x: null ?? 12
    assert_eq($x, 12)
}", out _);
    }
    [Test]
    public void CoalesceNullNull()
    {
        Run(@"
@start
event e {
    var $x: null ?? null
    assert_eq($x, null)
}", out _);
    }
    [Test]
    public void CoalesceIntNull()
    {
        Run(@"
@start
event e {
    var $x: 13 ?? null
    assert_eq($x, 13)
}", out _);
    }
    [Test]
    public void CoalesceStringInt()
    {
        Run(@"
@start
event e {
    var $x: 'asd' ?? 12
    assert_eq($x, 'asd')
    debug($x)
}", out _);
    }
}
public class IfTests : TestsBase
{
    [Test]
    public void IfTrue()
    {
        var db = Run(@"
entity T {}
prop p: number
event r {
    create $t: (T)
    if true {
        set $t.p = 1
    }
}", out _);
        db.RunAction("r");
        Assert.AreEqual(1, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }
    [Test]
    public void IfTrue_Expression()
    {
        var db = Run(@"
entity T {}
prop p: number
event r {
    create $t: (T)
    
    set $t.p = if true { 1 }
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
event r {
    create $t: (T)
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
event r {
    if false {
        create $x: (T)
    } else {
        create $x: (U)
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
event r {
    var $x: if false {
        create $x: (T)
    } else {
        create $x: (U)
    }
    set $x.p = 2
}", out _);
        db.RunAction("r");
        Assert.AreEqual(2, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }
}
