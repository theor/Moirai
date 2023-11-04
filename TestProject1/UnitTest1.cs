namespace TestProject1;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    public static Database Run(string s, out List<StoryParser.Error> errors, int errorCount = 0)
    {
        Console.WriteLine(s);
        var db = StoryParser.Parse(s, out errors);
        
        var printed = db.Printer.Print();
        Console.WriteLine("### REPRINT");
        Console.WriteLine(printed);
        Assert.AreEqual(errorCount, errors.Count, string.Join("\n", errors));
        if (errorCount == 0)
        {
            var reparsed = StoryParser.Parse(printed, out var errors2);
            Assert.AreEqual(errorCount, errors2.Count, "During reparse: " + string.Join(", ", errors2));
            // Console.WriteLine("### REPRINT 2");
            var print2 = reparsed.Printer.Print();
            // Console.WriteLine(print2);
            Assert.AreEqual(printed, print2);
        }
        return db;
    }

    [Test]
    public void Int_Set()
    {
        var s = @"
entity Person {}
prop f: number
rule r {
    create Person
    set f = 42
    assert $0.f = 42
}
rule rr {
    pick Person
    set f = 43
    assert $0.f = 43
}
";

        var db = Run(s, out var errors);
        db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[1]);
        db.PrintDb();
    }

    [Test]
    public void Int_Add() => RunAssert(@"
entity Person {}
prop f: number
rule r {
    create Person
    set f = 2 + 3
    assert $0.f = 5
}
");

    [Test]
    public void Int_AddMul_Precedence() => RunAssert(@"
entity Person {}
prop f: number
rule r {
    create Person
    set f = 2 + 3 * 4
    assert_eq $0.f, 14
}
");
   

    static void RunAssert(string s, string? actionName = null)
    {
        var db = Run(s, out var errors);
        if (actionName != null)
            db.RunAction(actionName);
        else
            db.RunAction(db.Actions[0]);
        db.PrintDb();
    }
    
    [Test]
    public void Int_FromEnum()
    {
        var s = @"
entity Person {}
enum E { A, B, C }
prop f: number
rule r {
    create Person
    set f = E.B
    assert $0.f = 1
    set f = E.C * 2
    assert $0.f = 4
}
";

        var db = Run(s, out var errors);
        db.RunAction(db.Actions[0]);
        db.PrintDb();
    }

    [Test]
    public void Int_Cmp()
    {
        var s = @"
entity Person {}
prop f: number
rule r {
    create Person
    set f = 42 > 4
    assert $0.f
}
";

        var db = Run(s, out var errors);
        db.RunAction(db.Actions[0]);
        db.PrintDb();
    }

    [Test]
    public void Int_Increment()
    {
        var s = @"
entity Person {}
prop f: number
rule r {
    create Person
    set f = 42
    set f = f + 1
    assert $0.f = 43
}
";

        var db = Run(s, out var errors);
        db.RunAction(db.Actions[0]);
        db.PrintDb();
    }
    [Test]
    public void Test1()
    {
        var s = @"
entity Person {}
prop alive: bool
rule born_char {
    create Person
    set alive = true
    assert $0.alive = true
}
";
        
        var db = Run(s, out var errors);
        
        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];
        Assert.AreEqual("born_char", action.Name);
        Assert.AreEqual(3, action.Effects.Count);
        
        Assert.IsInstanceOf<SetProperty>(action.Effects[1]);
        

        PropertyId propId = db.GetProperty("alive");
        Assert.IsTrue(propId.IsValid);
        // TODO reactivate
        // Assert.AreEqual(propId, ((SetProperty)action.Effects[1]).PropertySet.Property);
        db.RunAction(action.Name);
        db.PrintDb();
        Assert.AreEqual(1, db.Entities.Count());
    }
    
    [Test]
    public void TypeQuery()
    {
        var s = @"
entity Person {}
prop alive: bool
rule r {
    pick type = Person
    set alive = true
}
";
        
        var db = Run(s, out var errors);

        EntityTypeId typePerson = db.GetEntityType("Person").Id;
        db.AllocateEntity(typePerson, "A");
        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];

        PropertyId propId = db.GetProperty("alive");
        Assert.IsTrue(propId.IsValid);
        // TODO reactivate
        // Assert.AreEqual(propId, ((SetProperty)action.Effects[1]).PropertySet.Property);
        db.RunAction(action.Name);
        db.PrintDb();
        Assert.AreEqual(1, db.Entities.Count());
        Assert.AreEqual(true, db.Entities.Single().GetProperty(propId).BoolValue);
    }
    
    [Test]
    public void Test2()
    {
        var s = @"
entity Person {}
prop alive: bool
rule char_dies {
    pick $p: type = Person, alive = true
    set $p.alive = false
}";
        var db = Run(s, out var errors);
        
        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];
        Assert.AreEqual("char_dies", action.Name);
        Assert.AreEqual(2, action.Effects.Count);
    }
    [Test]
    public void Each()
    {
        var s = @"
entity Person {}
prop test: bool
rule foreach {
    each $x: type=Person {
        set $x.test = true
        format '{$x.name} {$x.test}'
    }
}";
        var db  =Run(s, out var errors);
        db.History = new();
        var propId = db.GetProperty("test");
        var typePerson = db.GetEntityType("Person");
        db.AllocateEntity(typePerson.Id, "A");
        db.AllocateEntity(typePerson.Id, "B");
        db.PrintDb();
        foreach (var entity in db.Entities)
        {
            Assert.IsFalse(entity.TryGetProperty(propId, out var val));
        }
        db.RunAction(db.Actions[0]);
        db.PrintDb();
        foreach (var entity in db.Entities)
        {
            Assert.IsTrue(entity.TryGetProperty(propId, out var val));
            Assert.AreEqual(true, val.BoolValue);
        }
        foreach (var changeset in db.History.Changesets)
        {
            db.Printer.PrintChangeset(changeset);
            Console.WriteLine(changeset.Description);
            
        }
    }
    [Test]
    public void Event()
    {
        var s = @"
entity Person {}
prop alive: bool
prop test: bool
rule born {
    create Person
    set alive = true
}

rule die {
    pick type=Person, alive = true
    set alive = false
}

event on_death {
    when alive = false

    set test = true
}";
        var db  = Run(s, out _, 0);
        Assert.AreEqual(1, db.Events.Count);

        db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[1]);
        db.PrintDb();
        Entity e = db.Entities.Single();
        PropertyId propTest = db.GetProperty("test");
        Assert.AreEqual(true, e.GetProperty(propTest).BoolValue);
    }
    
    [Test]
    public void Event2()
    {
        var s = @"
entity Person {}
entity Item {}
entity Link {}
prop alive: bool
prop child: ref
prop parent: ref
prop owner: ref

event inherit {
    when $p: type = Person, alive = false
    each $i: type = Item, owner = $p {
        pick $l: type = Link, $l.parent = $p 
        pick $c: type = Person, alive = true, id = $l.child
            set $i.owner = $c
            format ""{$c.name} inherits the {$i.name} from {$p.name}""
    }
}";
        var db  = Run(s, out _, 0);
        Assert.AreEqual(1, db.Events.Count);

        // db.RunAction(db.Events[0]);
        db.PrintDb();
        // Entity e = db.Entities.Single();
        // PropertyId propTest = db.GetProperty("test");
        // Assert.AreEqual(true, e.GetProperty(propTest).BoolValue);
    }
    [Test]
    public void Test3()
    {
        var s = @"
prop alive: bool
rule char_dies {
    pick $x: alive = true
    pick $y: id != $x
}";
       
        var db = Run(s, out var errors);
        
        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];
        Assert.AreEqual("char_dies", action.Name);
        Assert.AreEqual(2, action.Effects.Count);
        var e1 = action.Effects[0];
        var e2 = action.Effects[1];
        Assert.IsInstanceOf<AssignPick>(e1);
        
        Assert.IsInstanceOf<AssignPick>(e2);
        
        var pe1 = (AssignPick)e1;
        var pe2 = (AssignPick)e2;

        // Assert.AreEqual(Assign.PredicateParameterType.Predicate, pe1.Predicate);
        // Assert.AreEqual(Assign.PredicateParameterType.Predicate, pe2.Type);
        
        Assert.NotNull(pe1.Value);
        Assert.IsInstanceOf<BinaryOperator>(pe1.Value);
        Assert.NotNull(pe2.Value);
        Assert.IsInstanceOf<BinaryOperator>(pe2.Value);

    }[Test]
    public void TestPredicateRightIsVar()
    {
        var s = @"
@char_dies
    $x = pick()
    $y = pick(id != $x)
    $z = pick(id != $y)
";
        Console.WriteLine(s);
        var db = StoryParser.Parse(s, out var errors);
        Console.WriteLine(db.Printer.Print());
        Console.WriteLine(string.Join("\n", errors.Select(e => ToString())));
        Assert.AreEqual(0, errors.Count);
        
        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];
        Assert.AreEqual("char_dies", action.Name);
        Assert.AreEqual(3, action.Effects.Count);
        var e1 = action.Effects[0];
        var e2 = action.Effects[1];
        Assert.IsInstanceOf<AssignPick>(e1);
        
        Assert.IsInstanceOf<AssignPick>(e2);
        
        var pe1 = (AssignPick)e1;
        var pe2 = (AssignPick)e2;

        // Assert.AreEqual(Assign.PredicateParameterType.Predicate, pe1.Predicate);
        // Assert.AreEqual(Assign.PredicateParameterType.Predicate, pe2.Type);
        
  
    }
     [Test]
    public void TestWedding()
    {
        var s = @"
@wedding
    $x = pick(type=Person, alive = true, partner = null)
    $y = pick(type=Person, alive = true, partner = null, id != $x)
    set $x.partner = $y
    set $y.partner = $x
";
        Console.WriteLine(s);
        var db = StoryParser.Parse(s, out var errors);
        Console.WriteLine(db.Printer.Print());
        Console.WriteLine(string.Join("\n", errors.Select(e => ToString())));
        Assert.AreEqual(0, errors.Count);
        
        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];
        Assert.AreEqual("wedding", action.Name);
        Assert.AreEqual(4, action.Effects.Count);
        var e1 = action.Effects[0];
        var e2 = action.Effects[1];
        Assert.IsInstanceOf<AssignPick>(e1);
        
        Assert.IsInstanceOf<AssignPick>(e2);
        
        var pe1 = (AssignPick)e1;
        var pe2 = (AssignPick)e2;

        // Assert.AreEqual(ComputedValue.PredicateParameterType.Predicate, pe1.Type);
        // Assert.AreEqual(ComputedValue.PredicateParameterType.Predicate, pe2.Type);
        
        Assert.NotNull(pe1.Value);
        Assert.IsInstanceOf<And>(pe1.Value);
        Assert.NotNull(pe2.Value);
        Assert.IsInstanceOf<And>(pe2.Value);

    }


    [Test]
    public void ParseWholeFile()
    {
        var path = Path.GetFullPath("../../../../MoiraiCli/w.sg");
        Console.WriteLine(path);
        Assert.IsTrue(File.Exists(path));
        var db = StoryParser.Parse(File.ReadAllText(path), out var errors);
        Console.WriteLine("------------------");
        var format = db.Printer.Print();
        Console.WriteLine(format);
        Assert.AreEqual(0, errors.Count, string.Join(", ", errors));
        var db2 = StoryParser.Parse(format, out var errors2);
        Assert.AreEqual(0, errors2.Count, string.Join(", ", errors2));
 
    }
    
    [Test]
    public void DuplicateVarNoError()
    {
        var s = @"
entity Person {}
prop alive: bool
rule char_dies {
    pick $p: type=Person, alive = true
    pick $p: type=Person, alive = true
    set $p.alive = false
}";
       
        var db = Run(s, out var errors, 0);
       
    }
    [Test]
    public void Enum_Set()
    {
        string s = @"
entity Person {}
enum Job { None, Farmer, Smith }
prop job: Job

rule create {
    create Person
    set job = Job.Farmer
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.PrintDb();
        var e = db.Entities.Single();
        PropertyId jobProp = db.GetProperty("job");
        var value = e.GetProperty(jobProp);
        Assert.IsTrue(db.GetEnumDefinition("Job", out var enumDefinition));
        Assert.AreEqual(enumDefinition.ValueType, value.Type);
        Assert.AreEqual(PropertyValue.ValueBaseType.Enum, value.Type.BaseType);
        Assert.AreEqual(1, value.IntValue);
    }
    
    [Test]
    public void Enum_Set_CastInt()
    {
        string s = @"
entity Person {}
enum Job { None, Farmer, Smith }
prop job: Job

rule create {
    create Person
    set job = 1
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.PrintDb();
        var e = db.Entities.Single();
        PropertyId jobProp = db.GetProperty("job");
        var value = e.GetProperty(jobProp);
        Assert.IsTrue(db.GetEnumDefinition("Job", out var enumDefinition));
        Assert.AreEqual(enumDefinition.ValueType, value.Type);
        Assert.AreEqual(PropertyValue.ValueBaseType.Enum, value.Type.BaseType);
        Assert.AreEqual(1, value.IntValue);
    }
    
    [Test]
    public void Prop_WrongEnum()
    {
        string s = @"
entity Person {}
enum Job { None, Farmer, Smith }
prop job: Job

rule create {
    create Person
    set job = Asd
}
";
        var db = Run(s, out var errors, 1);
        db.RunAction("create");
        db.PrintDb();
        var e = db.Entities.Single();
        PropertyId jobProp = db.GetProperty("job");
        var value = e.GetProperty(jobProp);
    }
    
    [Test]
    public void AssignRandomEnum()
    {
        string s = @"
entity Person {}
enum Job { Farmer, Smith, Mayor }
prop job: Job

rule create {
    create Person
    set job = random Job
}
";
        var db = Run(s, out var errors);
        db.SetSeed(45);
        int count = 1;
        for (int i = 0; i < count; i++)
        {
            db.RunAction("create");

        }
        db.PrintDb();
        var e = db.Entities.First();
        PropertyId jobProp = db.GetProperty("job");
        var value = e.GetProperty(jobProp);
        Assert.IsTrue(db.GetEnumDefinition("Job", out var enumDefinition));
        Assert.AreEqual(enumDefinition.ValueType, value.Type);
        Assert.AreEqual(PropertyValue.ValueBaseType.Enum, value.Type.BaseType);
        Assert.AreEqual(2, value.IntValue);
    }
    [Test]
    public void AssignCreate()
    {
        var s = @"
entity Person {}
entity Faction {}
prop faction: ref
prop owner: ref
rule create_faction {
    pick $p: type=Person, faction = null
    create $f: Faction
    set $f.owner = $p
    set $p.faction = $f
}";
        var db = Run(s, out var errors);
        Assert.AreEqual(4, db.Actions[0].Effects.Count);
       
    }
    [Test]
    public void Format()
    {
        var s = @"
entity Person {
}
entity Faction {
}
prop owner: ref
rule create_faction {
    create $f: Faction, 'Faction of {random name}'
    create $g: Faction
    set $g.name = 'Circle of {random name}'
    create $p: Person
    set $p.name = '{random name}'
    set $f.owner = $p
    format '{$p.name} creates the {$f.name} to counter the {$g.name}'
    assert_eq '{$p.name} creates the {$f.name} to counter the {$g.name}', 'River creates the Faction of Cerelia to counter the Circle of Hecate'
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        db.Printer.PrintChangeset(db.History.Changesets[0], false);
        Console.WriteLine(db.History.Changesets[0].Description);
        Assert.AreEqual("River creates the Faction of Cerelia to counter the Circle of Hecate", db.History.Changesets[0].Description);
    }
    [Test]
    public void Format_TwoRandomNames()
    {
        var s = @"
entity Person {
}
prop owner: ref
rule create_faction {
    create $p: Person, '{random name}-{random name} of {random name}'
    assert_eq $p.name, 'Cerelia-Hecate of River'
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
    }
    [Test]
    public void FormatLiteral()
    {
        var s = @"
rule create_faction {
    format 'res {42} > {16} = {42 > 16}'
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        db.Printer.PrintChangeset(db.History.Changesets[0], false);
        Console.WriteLine(db.History.Changesets[0].Description);
        Assert.AreEqual("res 42 > 16 = true", db.History.Changesets[0].Description);
    }
    
    [Test]
    public void CallRule()
    {
        var s = @"
entity E {}
rule called {
    create E
}
rule call {
    call called
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[1]);
       Assert.AreEqual(1, db.Entities.Count());
    }
    [Test]
    public void CallRuleReturnValue()
    {
        var s = @"
entity E {}
prop x: number
rule called {
    create E
}
rule call {
    call $x: called
    call $y: called
    
    set $y.x = 42
    assert_eq $x, 1
    assert_eq $y, 2
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[1]);
        db.PrintDb();
       Assert.AreEqual(2, db.Entities.Count());
    }
    [Test]
    public void SetLiteral()
    {
        var s = @"
entity E {}
prop x: number

rule call {
    var $w = 42
    var $g: number = 43
    assert_eq $w, 42
    assert_eq $g, 43
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        db.PrintDb();
    }
    [Test]
    public void Singleton()
    {
        var s = @"
entity Time {}
prop year: number

rule create {
    create Time, 'time'
    set year = 1000
}
rule read {
    assert_eq #Time.year, 1000
}";
        
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[1]);
        db.PrintDb();
    }

    [Test]
    public void Time()
    {
        var s = @"
entity Time {}
prop year: number
entity Person {}
enum Age { Child, Young, Adult, Old }
prop alive: bool
prop birthdate: number
prop age: Age
rule create_time {
    create Time, 'time'
    set year = 1000
}
rule born {
    create $p: Person
    set $p.alive = true
    set $p.age = Age.Child
    set $p.birthdate = #Time.year
    format 'The {$p.age} {$p.name} is born in {$p.birthdate}'
}

rule pass_15_years {
    set #Time.year = #Time.year + 15
    each $p: type=Person, alive = true, age = Age.Child, (birthdate + 20) <= #Time.year{
        set $p.age = Age.Young
    }
    each $p: type=Person, alive = true, age = Age.Young, (birthdate + 40) <= #Time.year{
        set $p.age = Age.Adult
    }
    each $p: type=Person, alive = true, age = Age.Adult, (birthdate + 60) <= #Time.year{
        set $p.age = Age.Old
    }
    each $p: type=Person, alive = true, age = Age.Old, (birthdate + 80) <= #Time.year{
        set $p.alive = false
    }
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[1]);
        db.PrintDb();
        db.RunAction(db.Actions[1]);
        db.RunAction(db.Actions[2]);
        db.PrintDb();
        db.RunAction(db.Actions[1]);
        db.RunAction(db.Actions[2]);
        db.RunAction(db.Actions[2]);
        db.PrintDb();
        db.RunAction(db.Actions[1]);
        db.RunAction(db.Actions[2]);
        db.RunAction(db.Actions[2]);
        db.PrintDb();
        db.RunAction(db.Actions[1]);
        db.RunAction(db.Actions[2]);
        db.PrintDb();
    }

    [Test]
    public void Time2()
    {
        var s = @"
entity Time {}
prop year: number
entity Person {}
enum Age { Child, Young, Adult, Old }
prop alive: bool
prop birthdate: number
prop age: Age
rule create_time {
    create Time, 'time'
    set year = 1000
}
rule born {
    pick $t: type=Time
    create $p: Person
    set $p.alive = true
    set $p.age = Age.Child
    set $p.birthdate = $t.year
    format 'The {$p.age} {$p.name} is born in {$p.birthdate}'
}

rule pass_15_years {
    pick $t: type=Time
    set $t.year = $t.year + 15
    each $p: type=Person, alive = true, age < Age.Old, (birthdate + 20* (age+1) ) <= $t.year, age < Age.Old {
        set $p.age = age+1
    }
   
    each $p: type=Person, alive = true, age = Age.Old, (birthdate + 80) <= $t.year{
        set $p.alive = false
    }
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[1]);
        db.PrintDb();
        db.RunAction(db.Actions[2]);
        db.RunAction(db.Actions[2]);
        db.PrintDb();
        db.RunAction(db.Actions[2]);
        db.PrintDb();
        db.RunAction(db.Actions[2]);
        db.PrintDb();
        db.RunAction(db.Actions[2]);
        db.RunAction(db.Actions[2]);
        db.PrintDb();
        db.PrintHistory();
    }

    void AssertInstruction(System.Action a)
    {
        try
        {
            a();
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine(e.Message);
            return;
        }
        Assert.Fail("Should have thrown");
    }
    void NoAssertInstruction(System.Action a)
    {
        a();
    }
    [Test]
    public void TestAssertFalse()
    {
        var s = @"
rule r {
    assert false
}";
        var db = Run(s, out var errors);
        db.History = new();
        
        AssertInstruction(() => db.RunAction(db.Actions[0]));
        db.PrintDb();
    }
    [Test]
    public void TestAssertEq()
    {
        var s = @"
rule r {
    assert 1 = 2
}";
        var db = Run(s, out var errors);
        db.History = new();
        
        AssertInstruction(() => db.RunAction(db.Actions[0]));
        db.PrintDb();
    }
    [Test]
    public void TestAssertTrue()
    {
        var s = @"
rule r {
    assert true
}";
        var db = Run(s, out var errors);
        db.History = new();
        
        NoAssertInstruction(() => db.RunAction(db.Actions[0]));
        db.PrintDb();
    }
}