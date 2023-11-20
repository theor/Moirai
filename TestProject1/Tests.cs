namespace TestProject1;

public class Tests : TestsBase
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Int_Set()
    {
        var s = @"
entity Person {}
prop f: number
rule r {
    create $p: Person
    set $p.f = 42
    assert $p.f = 42
}
rule rr {
    pick $p: Person
    set $p.f = 43
    assert $p.f = 43
}
";

        var db = Run(s, out var errors);
        db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[1]);
        db.Printer.PrintDb();
    }

    [Test]
    public void Int_Add() => RunAssert(@"
entity Person {}
prop f: number
rule r {
    create $p: Person
    set $p.f = 2 + 3
    assert $p.f = 5
}
");

    [Test]
    public void Int_Negative() => RunAssert(@"
entity Person {}
prop f: number
rule r {
    create $p: Person
    set $p.f = -2 + 3
    assert $p.f = 1
}
");

    [Test]
    public void Int_Negative2() => RunAssert(@"
entity Person {}
prop f: number
rule r {
    create $p: Person
    set $p.f = -4 - -3
    assert $p.f = -1
}
");

    [Test]
    public void Float_Add() => RunAssert(@"
entity Person {}
prop f: float
rule r {
    create $p: Person
    set $p.f = 2.1 + 3.2
    assert $p.f = 5.3
}
");


    [Test]
    public void Float_Floor() => RunAssert(@"
entity Person {}
prop f: float
rule r {
    create $p: Person
    set $p.f = floor(2.1 + 3.2)
    assert $p.f = 5
}
");

    [Test]
    public void Int_AddMul_Precedence() => RunAssert(@"
entity Person {}
prop f: number
rule r {
    create $p: Person
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
        db.Printer.PrintDb();
    }

    [Test]
    public void Int_FromEnum()
    {
        var s = @"
entity Person {}
enum E { A, B, C }
prop f: number
rule r {
    create $p: Person
    set f = E.B
    assert $0.f = 2
    set f = E.C * 2
    assert $0.f = 6
}
";

        var db = Run(s, out var errors);
        db.RunAction(db.Actions[0]);
        db.Printer.PrintDb();
    }

    [Test]
    public void Int_Cmp()
    {
        var s = @"
entity Person {}
prop f: number
rule r {
    create $p: Person
    set f = 42 > 4
    assert $0.f
}
";

        var db = Run(s, out var errors);
        db.RunAction(db.Actions[0]);
        db.Printer.PrintDb();
    }

    [Test]
    public void Int_Increment()
    {
        var s = @"
entity Person {}
prop f: number
rule r {
    create $p: Person
    set f = 42
    set f = f + 1
    assert $0.f = 43
}
";

        var db = Run(s, out var errors);
        db.RunAction(db.Actions[0]);
        db.Printer.PrintDb();
    }

    [Test]
    public void Test1()
    {
        var s = @"
entity Person {}
prop alive: bool
rule born_char {
    create $p: Person, '{random Name}'
    set $p.alive = true
    assert $p.alive = true
}
";

        var db = Run(s, out var errors);

        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];
        Assert.AreEqual("born_char", action.Name);
        Assert.AreEqual(3, action.Effects.Count);

        Assert.IsInstanceOf<SetProperty>(action.Effects[1]);


        PropertyId propId = db.GetPropertyId("alive");
        Assert.IsTrue(propId.IsValid);
        db.RunAction(action.Name);
        db.Printer.PrintDb();
        // db.Commit();
        Assert.AreEqual(1, db.Entities.Count());
    }

    [Test]
    public void FilterByType()
    {
        var s = @"
entity Person {}
prop alive: number
rule born_char {
    create $p: Person, '{random Name}'
}
@start rule init {
    call born_char, 10
}
rule r {
    each $p: type=Person {
        set $p.alive = 2
    }
}
";

        var db = Run(s, out var errors);

        // db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[2]);
        db.Printer.PrintDb();
        // db.Commit();
        Assert.AreEqual(10, db.Entities.Count());
        Assert.AreEqual(2, db.Entities.Last().GetProperty(db.GetPropertyId("alive")).IntValue);
    }

    [Test]
    public void TypeQuery()
    {
        var s = @"
entity Person {}
prop alive: bool
rule r {
    pick $p: type = Person
    set $p.alive = true
}
";

        var db = Run(s, out var errors);

        EntityTypeId typePerson = db.GetEntityType("Person").Id;
        db.AllocateEntity(typePerson, "A");
        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];

        PropertyId propId = db.GetPropertyId("alive");
        Assert.IsTrue(propId.IsValid);
        db.RunAction(action.Name);
        db.Printer.PrintDb();
        db.Commit();
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
        record '{$x.name} {$x.test}'
    }
}";
        var db = Run(s, out var errors);
        db.History = new();
        var propId = db.GetPropertyId("test");
        var typePerson = db.GetEntityType("Person");
        db.AllocateEntity(typePerson.Id, "A");
        db.AllocateEntity(typePerson.Id, "B");
        db.Printer.PrintDb();
        foreach (var entity in db.Entities)
        {
            Assert.IsFalse(entity.TryGetProperty(propId, out var val));
        }

        db.RunAction(db.Actions[0]);
        db.Printer.PrintDb();
        foreach (var entity in db.Entities)
        {
            Assert.IsTrue(entity.TryGetProperty(propId, out var val));
            Assert.AreEqual(true, val.BoolValue);
        }

        foreach (var changeset in db.History.Changesets)
        {
            db.Printer.PrintChangeset(changeset);
        }
    }
    
    [Test]
    public void Each_Multiple()
    {
        var s = @"
entity Person {}
prop x: bool
prop y: bool
rule foreach {
    each $x: type=Person {
        set $x.x = true
        record '{$x.name} {$x.x}'
    }
    each $x: type=Person {
        set $x.y = true
        record '{$x.name} {$x.y}'
    }
}";
        var db = Run(s, out var errors, src => Assert.That(src.Contains("$1") || src.Contains("$2"), Is.False));
        db.History = new();
        var propX = db.GetPropertyId("x");
        var propY = db.GetPropertyId("y");
        var typePerson = db.GetEntityType("Person");
        db.AllocateEntity(typePerson.Id, "A");
        db.AllocateEntity(typePerson.Id, "B");
        db.Printer.PrintDb();
       
        db.RunAction(db.Actions[0]);
        db.Printer.PrintDb();
        foreach (var entity in db.Entities)
        {
            Assert.IsTrue(entity.TryGetProperty(propX, out var val));
            Assert.AreEqual(true, val.BoolValue);
            Assert.IsTrue(entity.TryGetProperty(propY, out  val));
            Assert.AreEqual(true, val.BoolValue);
        }

        foreach (var changeset in db.History.Changesets)
        {
            db.Printer.PrintChangeset(changeset);
        }
    }
    
    [Test]
    public void Each_Multiple_Unnamed()
    {
        var s = @"
entity Person {}
prop x: bool
prop y: bool
rule foreach {
    each $p: type=Person {
        set $p.x = true
        record '{$p.name} {$p.x}'
    }
    each $p: type=Person {
        set $p.y = true
        record '{$p.name} {$p.y}'
    }
}";
        var db = Run(s, out var errors, src => Assert.That(src.Contains("$1") || src.Contains("$2"), Is.False));
        db.History = new();
        var propX = db.GetPropertyId("x");
        var propY = db.GetPropertyId("y");
        var typePerson = db.GetEntityType("Person");
        db.AllocateEntity(typePerson.Id, "A");
        db.AllocateEntity(typePerson.Id, "B");
        db.Printer.PrintDb();
       
        db.RunAction(db.Actions[0]);
        db.Printer.PrintDb();
        foreach (var entity in db.Entities)
        {
            Assert.IsTrue(entity.TryGetProperty(propX, out var val));
            Assert.AreEqual(true, val.BoolValue);
            Assert.IsTrue(entity.TryGetProperty(propY, out  val));
            Assert.AreEqual(true, val.BoolValue);
        }

        foreach (var changeset in db.History.Changesets)
        {
            db.Printer.PrintChangeset(changeset);
        }
    }

    [Test]
    [TestCase("../../../../MoiraiCli/w.sg")]
    [TestCase("../../../../MoiraiCli/space.sg")]
    public void ParseWholeFile(string rpath)
    {
        var path = Path.GetFullPath(rpath);
        Console.WriteLine(path);
        Assert.IsTrue(File.Exists(path));
        var db = StoryParser.Parse(File.ReadAllText(path), out var errors);
        Console.WriteLine("------------------");
        var record = db.Printer.Print();
        Console.WriteLine(record);
        Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        var db2 = StoryParser.Parse(record, out var errors2);
        Assert.AreEqual(0, errors2.Count, string.Join("\n", errors2));
        db.Init();
        db.Ctx.PassYears(100, true);
        db.Commit();
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

    [Test, Ignore("Nested property paths not implemented yet")]
    public void PropertyPath_Nested()
    {
        string s = @"
entity Person {}
prop x: number
prop link: ref

rule create {
    create $p: Person
    create $p2: Person
    set $p.link = $p2
    set $p2.x = 33
    assert_eq 33, $p.link.x
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.Printer.PrintDb();
    }

    [Test]
    public void PropertyPath_Var()
    {
        string s = @"
entity Person {}
prop x: number
prop link: ref

rule create {
    create $p: Person
    create $p2: Person
    set $p.link = $p2
    set $p2.x = 33
    var $tmp: $p.link
    assert_eq 33, $tmp.x
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.Printer.PrintDb();
    }


    [Test]
    public void Enum_Set()
    {
        string s = @"
entity Person {}
enum Job { None, Farmer, Smith }
prop job: Job

rule create {
    create $p: Person
    set $p.job = Job.Farmer
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.Printer.PrintDb();
        var e = db.Entities.Single();
        PropertyId jobProp = db.GetPropertyId("job");
        var value = e.GetProperty(jobProp);
        Assert.IsTrue(db.GetEnumDefinition("Job", out var enumDefinition));
        Assert.AreEqual(enumDefinition.ValueType, value.Type);
        Assert.AreEqual(PropertyValue.ValueBaseType.Enum, value.Type.BaseType);
        Assert.AreEqual(2, value.IntValue);
    }

    [Test]
    public void Enum_Set_CastInt()
    {
        string s = @"
entity Person {}
enum Job { None, Farmer, Smith }
prop job: Job

rule create {
    create $p: Person
    set $p.job = 1
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.Printer.PrintDb();
        var e = db.Entities.Single();
        PropertyId jobProp = db.GetPropertyId("job");
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
    create $p: Person
    set job = Asd
}
";
        var db = Run(s, out var errors, 1);
        db.RunAction("create");
        db.Printer.PrintDb();
        var e = db.Entities.Single();
        PropertyId jobProp = db.GetPropertyId("job");
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
    create $p: Person
    set $p.job = random Job
}
";
        var db = Run(s, out var errors);
        db.SetSeed(45);
        int count = 1;
        for (int i = 0; i < count; i++)
        {
            db.RunAction("create");
        }

        db.Printer.PrintDb();
        var e = db.Entities.First();
        PropertyId jobProp = db.GetPropertyId("job");
        var value = e.GetProperty(jobProp);
        Assert.IsTrue(db.GetEnumDefinition("Job", out var enumDefinition));
        Assert.AreEqual(enumDefinition.ValueType, value.Type);
        Assert.AreEqual(PropertyValue.ValueBaseType.Enum, value.Type.BaseType);
        Assert.AreEqual(3, value.IntValue);
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
    create $f: Faction, 'Faction of {random Name}'
    create $g: Faction
    set $g.name = 'Circle of {random Name}'
    create $p: Person
    set $p.name = '{random Name}'
    set $f.owner = $p
    record '{$p.name} creates the {$f.name} to counter the {$g.name}'
    assert_eq '{$p.name} creates the {$f.name} to counter the {$g.name}', 'River creates the Faction of Cerelia to counter the Circle of Hecate'
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        Console.WriteLine(db.Records.Last().Text);
        Assert.AreEqual("<#3>River</> creates the <#1>Faction of Cerelia</> to counter the <#2>Circle of Hecate</>",
            db.Records.Last().Text);
    }

    [Test]
    public void Format_TwoRandomNames()
    {
        var s = @"
entity Person {
}
prop owner: ref
rule create_faction {
    create $p: Person, '{random Name}-{random Name} of {random Name}'
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
    record 'res {42} > {16} = {42 > 16}'
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        Console.WriteLine(db.Records[0]);


        Console.WriteLine(db.Records[0].Text);
        Assert.AreEqual("res 42 > 16 = true", db.Records[0].Text);
    }

    [Test]
    public void CallRule()
    {
        var s = @"
entity E {}
rule called {
    create $e: E
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
    create $x: E
}
rule call {
    var $x: call called
    var $y: call called
    
    set $y.x = 42
    assert_eq $x, 1
    assert_eq $y, 2
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[1]);
        db.Printer.PrintDb();
        Assert.AreEqual(2, db.Entities.Count());
    }

    [Test]
    public void SetLiteral()
    {
        var s = @"
entity E {}
prop x: number

rule call {
    var $w:  42
    var $g: 43
    assert_eq $w, 42
    assert_eq $g, 43
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        db.Printer.PrintDb();
    }

    [Test]
    public void Singleton()
    {
        var s = @"
rule create {
    create $t: Time, 'time'
    set year = 1000
}
rule read {
    assert_eq #Time.year, 1000
}";

        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[1]);
        db.Printer.PrintDb();
    }

    [Test]
    public void CheckRandomEvent([Values(1ul, 2ul, 3ul, 4ul, 5ul, 6ul, 7ul)] ulong seed)
    {
        string s = @"
entity Person {}

rule init {
    create $t: Time, 'time'
    set $t.year = 345
}
@4 per 1 years
rule born {
    create $p: Person
}
";
        var db = Run(s, out _);
        db.SetSeed(seed);
        db.RunAction("init");

        db.Ctx.PassYears(10, true);
        db.Printer.PrintDb();
        Console.WriteLine("Born: " + (db.Entities.Count() - 1));
    }

    [Test]
    public void Time()
    {
        var s = @"
entity Person {}
enum Age { Child, Young, Adult, Old }
prop alive: bool
prop birthdate: number
prop age: Age
rule create_time {
    create $t: Time, 'time'
    set $t.year = 1000
}
rule born {
    create $p: Person
    set $p.alive = true
    set $p.age = Age.Child
    set $p.birthdate = #Time.year
    record 'The {$p.age} {$p.name} is born in {$p.birthdate}'
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
        db.Printer.PrintDb();
        db.RunAction(db.Actions[1]);
        db.RunAction(db.Actions[2]);
        db.Printer.PrintDb();
        db.RunAction(db.Actions[1]);
        db.RunAction(db.Actions[2]);
        db.RunAction(db.Actions[2]);
        db.Printer.PrintDb();
        db.RunAction(db.Actions[1]);
        db.RunAction(db.Actions[2]);
        db.RunAction(db.Actions[2]);
        db.Printer.PrintDb();
        db.RunAction(db.Actions[1]);
        db.RunAction(db.Actions[2]);
        db.Printer.PrintDb();
    }

    [Test]
    public void SetVar2XEqVar1Y()
    {
        string s = @"
entity Person {}
prop birthdate: number
rule born {
    pick $t: type=Time
    create $p: Person
    set $p.birthdate = $t.year
}";
        Run(s, out _);
    }

    [Test]
    public void SetVar2XEqVar1Y_ImplicitVar()
    {
        string s = @"
entity Person {}
prop birthdate: number
rule born {
    pick $t: type=Time
    create $p: Person
    set birthdate = $t.year
}";
        Run(s, out _);
    }

    [Test]
    public void Time2()
    {
        var s = @"
entity Person {}
enum Age { Child, Young, Adult, Old }
prop alive: bool
prop birthdate: number
prop age: Age
rule create_time {
    create $t: Time, 'time'
    set $t.year = 1000
}
rule born {
    pick $t: type=Time
    create $p: Person
    set $p.alive = true
    set $p.age = Age.Child
    set $p.birthdate = $t.year
    record 'The {$p.age} {$p.name} is born in {$p.birthdate}'
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
        db.Printer.PrintDb();
        db.Commit();
        Assert.AreEqual(1000, db.Entities.Last().GetProperty(db.GetPropertyId("birthdate")).IntValue);
        db.RunAction(db.Actions[2]);
        db.RunAction(db.Actions[2]);
        db.Printer.PrintDb();
        db.RunAction(db.Actions[2]);
        db.Printer.PrintDb();
        db.RunAction(db.Actions[2]);
        db.Printer.PrintDb();
        db.RunAction(db.Actions[2]);
        db.RunAction(db.Actions[2]);
        db.Printer.PrintDb();
        db.Printer.PrintHistory(db);
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
        db.Printer.PrintDb();
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
        db.Printer.PrintDb();
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
        db.Printer.PrintDb();
    }
}
