using Moirai.Parser;

namespace TestProject1;

public class InstanceFunctionTests : TestsBase
{
    [Test]
    public void ConstantFunction()
    {
        var s = @"
entity Person {
    function f(): number {
        2
    }
}

@start
event start {
    create Person $p: 'test'
    debug('{$p.f()}')
    assert_eq(2, $p.f())
}";
        var db = Run(s, out _, 0);
    }

    [Test]
    public void ComputedProperty()
    {
        var s = @"
entity Person {
    prop age: number
    function double_age(): number {
        $self.age * 2
    }
}

@start
event start {
    var $y: 2
    create Person $p: 'test'
    set $p.age = 10
    debug('{$p.double_age()}')
    assert_eq(20, $p.double_age())
}";
        var db = Run(s, out _, 0);
    }
    
    [Test]
    public void ComputedPropertyWithParam()
    {
        var s = @"
entity Person {
    prop age: number
    function add_age($x:number): number {
        $self.age + $x
    }
}

@start
event start {
    var $y: 2
    create Person $p: 'test'
    set $p.age = 10
    debug('{$p.add_age(5)}')
    assert_eq(15, $p.add_age(5))
}";
        var db = Run(s, out _, 0);
    }
    [Test]
    public void ComputedProperty2()
    {
        var s = @"
entity Person {
    prop age: number
    function is_alive(): bool {
        $self.age > 0
    }
}

@start
event start {
    var $y: 2
    create Person $p: 'test'
    set $p.age = 10
    debug('{$p.is_alive()}')
    assert_eq(true, $p.is_alive())
    set $p.age = 0
    debug('{$p.is_alive()}')
    assert_eq(false, $p.is_alive())
}";
        var db = Run(s, out _, 0);
    }
    
    [Test]
    public void FunctionInQuery()
    {
        var s = @"
entity Person {
    prop age: number
    function f(): number {
        $self.age
    }
}


@start
event start {
    create Person $p: 'test'
    set $p.age = 10
    if pick Person $p: ($p.f() > 5) {
        debug('ok {$p}')
    } else {
        debug(false)
    }
}";
        var db = Run(s, out _, 0);
        db.History = new();
        // db.Printer.PrintDb();
    }

}
public class FunctionTests : TestsBase
{
    [Test]
    public void ConstantFunction()
    {
        var s = @"
function f(): number {
    2
}

@start
event start {
    debug('{f()}')
}";
        var db = Run(s, out _, 0);
    }
    [Test]
    public void MissingReturnValue()
    {
        var s = @"
function f(): number {
    debug(2)
}

@start
event start {
    debug('{f()}')
}";
        var db = Run(s, out var errors, 1);
        Assert.That(StoryParser.ErrorCode.MissingReturnValue, Is.EqualTo(errors[0].Code));
    }  [Test]
    public void WrongReturnType()
    {
        var s = @"
function f(): number {
    'asd'
}

@start
event start {
    debug('{f()}')
}";
        var db = Run(s, out var errors, 1);
        Assert.That(StoryParser.ErrorCode.MismatchedReturnType, Is.EqualTo(errors[0].Code));
    }
    [Test]
    public void MissingParamFunction()
    {
        var s = @"
function f($x: number): number {
    2 * $x
}

@start
event start {
    debug('{f()}')
}";
        var db = Run(s, out var errors, 1);
        Assert.AreEqual(StoryParser.ErrorCode.MissingArgument, errors[0].Code);
    }
    [Test]
    public void WrongParamType()
    {
        var s = @"
entity Person {
    prop age: number
}
function f($x: Person): number {
    $x.age
}

@start
event start {
    debug('{f('asd')}')
}";
        var db = Run(s, out var errors, 1);
        Assert.AreEqual(StoryParser.ErrorCode.MismatchedAssignmentTypes, errors[0].Code);
    }
    [Test]
    public void OneParamFunction()
    {
        var s = @"
function f($x: number): number {
    2 * $x
}

@start
event start {
    debug('{f(3)}')
}";
        var db = Run(s, out _, 0);
        db.History = new();
        // db.Printer.PrintDb();
    }
    [Test]
    public void TwoParamFunction()
    {
        var s = @"
function f($x: number, $y: number): number {
    $y * $x
}

@start
event start {
    debug('{f(3,4)}')
}";
        var db = Run(s, out _, 0);
        db.History = new();
        // db.Printer.PrintDb();
    }
    [Test]
    public void FunctionEntityParam()
    {
        var s = @"
entity Person {
    prop age: number
}
function f($x: Person): number {
    $x.age
}

@start
event start {
    create Person $p: 'test'
    set $p.age = 10
    debug('{f($p)}')
}";
        var db = Run(s, out _, 0);
        db.History = new();
        // db.Printer.PrintDb();
    }
    
    [Test]
    public void FunctionInQuery()
    {
        var s = @"
entity Person {
    prop age: number
}
function f($x: Person): number {
    $x.age
}

@start
event start {
    create Person $p: 'test'
    set $p.age = 10
    if pick Person $p: (f($p) > 5) {
        debug('ok {$p}')
    } else {
        debug(false)
    }
}";
        var db = Run(s, out _, 0);
        db.History = new();
        // db.Printer.PrintDb();
    }
    
    [Test]
    public void FunctionEntityParamExpression()
    {
        var s = @"
entity Person {
    prop age: number
    prop alive: bool
}
function f($x: Person): bool {
    $x.age > 0 and $x.alive
}

@start
event start {
    create Person $p: 'test'
    set $p.age = 10
    debug('{f($p)}')
    set $p.alive = true
    debug('{f($p)}')
}";
        var db = Run(s, out _, 0);
        db.History = new();
        // db.Printer.PrintDb();
    }
}
