namespace TestProject1;

public class NumberTests : TestsBase
{
    [Test]
    public void Int_Add() => RunAssert(@"
entity Person {}
prop f: number
event r {
    create $p: (Person)
    set $p.f = 2 + 3
    assert $p.f = 5
}
");

    [Test]
    public void Int_Negative() => RunAssert(@"
entity Person {}
prop f: number
event r {
    create $p: (Person)
    set $p.f = -2 + 3
    assert $p.f = 1
}
");

    [Test]
    public void Int_Negative2() => RunAssert(@"
entity Person {}
prop f: number
event r {
    create $p: (Person)
    set $p.f = -4 - -3
    assert $p.f = -1
}
");

    [Test]
    public void Float_Add() => RunAssert(@"
entity Person {}
prop f: float
event r {
    create $p: (Person)
    set $p.f = 2.1 + 3.2
    assert $p.f = 5.3
}
");


    [Test]
    public void Float_Floor() => RunAssert(@"
entity Person {}
prop f: float
event r {
    create $p: (Person)
    set $p.f = floor(2.1 + 3.2)
    assert $p.f = 5
}
");

    [Test]
    public void Int_AddMul_Precedence() => RunAssert(@"
entity Person {}
prop f: number
event r {
    create $p: (Person)
    set f = 2 + 3 * 4
    assert_eq ($0.f, 14)
}
");


    [Test]
    public void Percent_PropertyDefinition()
    {
        Run(@"
prop p: percentage
entity E
@start
event e {
    create $p: E
    set $p.p = 50%
    assert_eq(50%, $p.p)
}", out _);
    }
    [Test]
    public void Percent_SetProperty_CheckTypeIsPercentage()
    {
        Run(@"
prop p: percentage
entity E
@start
event e {
    create $p: E
    set $p.p = 50% + 10
    assert_eq(60%, $p.p)
    debug($p.p)
}", out _);
    }
    [Test]
    public void Percent_PickPropertyDefinition()
    {
        Run(@"
prop p: percentage
entity E
@start
event e {
    create $p: E
    set $p.p = 50%
    create $p2: E
    set $p2.p = 60%
    assert(pick $x: (type = E, p < 55%))
    
    debug($x)
}", out _);
    }

    [Test]
    public void Percent_Print()
    {
        Run(@"
@start
event e {
    var $p: 50%
    debug($p)
    assert_eq($p, 50%)
}", out _);
    }
    [Test]
    public void Percent_Add()
    {
        Run(@"
@start
event e {
    var $p: 50% + 2%
    debug($p)
    assert_eq($p, 52%)
}", out _);
    }
    [Test]
    public void Percent_Add_Clamp_100()
    {
        Run(@"
@start
event e {
    var $p: 50% + 60%
    debug($p)
    assert_eq($p, 100%)
}", out _);
    }
    [Test]
    public void Percent_Add_Clamp_0()
    {
        Run(@"
@start
event e {
    var $p: 50% - 60%
    debug($p)
    assert_eq($p, 0%)
}", out _);
    }
}
