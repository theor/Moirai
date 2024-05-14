using System.Data;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Moirai;
using NUnit.Framework.Internal;

namespace TestProject1;

[Ignore("Manual tests")]
public class SQLiteTests
{
    [Test]
    public void PerfTest()
    {
         string path = Path.GetFullPath("../../../../MoiraiCli/hello.db");
         Console.WriteLine(path + " "+ File.Exists(path));
        using var n = new SqliteConnection($"Data Source=\"{path}\"");
        n.Open();
        var res = new List<long>();
        var sw = Stopwatch.StartNew();
        var l = 1000;
        for (int i = 0; i < l; i++)
        {
            if(i == 500)
                sw.Restart();
            var cmd = n.CreateCommand();
            cmd.CommandText = $"SELECT id FROM entity WHERE type = 2 AND god = {i}";
            var reader = cmd.ExecuteReader();
            while (reader.Read())
                res.Add(reader.GetInt64(0));
        }
        var ms = sw.ElapsedMilliseconds;
        Console.WriteLine((ms / (float)500) + "ms");
        Console.WriteLine(string.Join(",", res));
    }
    [Test]
    public void PerfTest_Prepared()
    {
         string path = Path.GetFullPath("../../../../MoiraiCli/hello.db");
         Console.WriteLine(path + " "+ File.Exists(path));
        using var n = new SqliteConnection($"Data Source=\"{path}\"");
        n.Open();
        var res = new List<long>();
        var l = 1000;
        
        var cmd = n.CreateCommand();
        cmd.CommandText = "SELECT id FROM entity WHERE type = 2 AND god = $god";
        var godParam = cmd.Parameters.Add("$god", SqliteType.Integer);
        cmd.Prepare();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < l; i++)
        {
            if(i == 500)
                sw.Restart();
            godParam.Value = i;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                res.Add(reader.GetInt64(0));
        }
        var ms = sw.ElapsedMilliseconds;
        Console.WriteLine((ms / (float)500) + "ms");
        Console.WriteLine(string.Join(",", res));
    }
    [Test]
    public void RandomFunction()
    {
         string path = Path.GetFullPath("../../../../MoiraiCli/hello.db");
         Console.WriteLine(path + " "+ File.Exists(path));
        using var n = new SqliteConnection($"Data Source=\"{path}\"");
        Pcg32 pcg = new Pcg32(32, 41);
        n.CreateFunction("rnd", () => pcg.GenerateNext());
        n.Open();
        var res = new List<long>();
        var l = 2000;
        
        var cmd = n.CreateCommand();
        cmd.CommandText = "SELECT id, rnd() as r FROM entity WHERE type = 2 AND god = $god ORDER BY r LIMIT 1";
        var godParam = cmd.Parameters.Add("$god", SqliteType.Integer);
        godParam.Value = 21;
        cmd.Prepare();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < l; i++)
        {
            if(i == 500)
                sw.Restart();
            pcg = new Pcg32(21, 42);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                res.Add(reader.GetInt64(0));
        }
        var ms = sw.ElapsedMilliseconds;
        Console.WriteLine((ms / (float)1500) + "ms");
        Console.WriteLine(string.Join(",", res));
    }
}

public class DeclarationTests : TestsBase
{
    [Test]
    public void EachVariableShouldNotExistAfterScope()
    {
        var db = Run(@"
entity A {
    prop x: number
}
event e {
    create A $asd: 'asd'
    each A $a { }
    debug $a
}
", out var errors, 1);
        db.RunAction("e");
        Assert.AreEqual(1, errors.Count);
    }   [Test]
    public void PickVariableShouldExistAfterScope()
    {
        var db = Run(@"
entity A {
    prop x: number
}
event e {
    create A $asd: 'asd'
    pick A $a { }
    debug $a
}
", out var errors, 0);
        db.RunAction("e");
    }
    [Test]
    public void EachVariableShouldNotExistAfterScope3()
    {
        var db = Run(@"
entity Person {
    prop alive: bool
}
entity Item {
    prop x: number
}
event e {
   each Person $child {
        record ''
    }
    each Item $i {
        pick Person $child: (alive)
    }
}
", out var errors);
        db.RunAction("e");
        Assert.AreEqual(0, errors.Count);
    }
    [Test]
    public void ReuseVariableName()
    {
        var db = Run(@"
entity A {
    prop x: number
}
event e {
    var $a: 12
    each A $b: (x = 32) {
        pick A $a: (x = 11)
        record ''
    }
    
}", out _);
        db.RunAction("e");
    }
    
    [Test]
    public void ReusePropertyName()
    {
        var db = Run(@"
entity A {
    prop x: number
}
entity B {
    prop x: number
}
event e {
    create A $a
    set $a.x = 1
    create B $b
    set $b.x = 2
    assert_eq($a.x, 1)
    assert_eq($b.x, 2)
    
}", out _);
        db.RunAction("e");
    }
    [Test]
    public void SetPropertyOfWrongType_ShouldThrow()
    {
        string s = @"
entity A {
    prop x: number
}
entity B {
    prop y: number
}

event create {
    create A $p
    set y = 12
}
";
        Run(s, out var e, 2);
    }
    [Test]
    public void SetPropertyOfRightType()
    {
        string s = @"
entity A {
    prop x: number
}

event create {
    create A $p
    set x = 12
}
";
        Run(s, out var e, 0);
    }
    [Test]
    public void SetEnumProperty_UnknownEnum()
    {
        string s = @"
entity Person {
    prop job: Job
}
enum Job { None, Farmer, Smith }

event create {
    create Person $p: ()
    set job = Asd
}
";
        var db = Run(s, out var errors, 2);
        db.RunAction("create");
        db.Printer.PrintDb();
        var e = db.Entities.Single();
        PropertyId jobProp = db.GetPropertyId("Person","job");
        var value = e.GetProperty(jobProp);
    } [Test]
    public void SetEnumProperty_WrongEnum()
    {
        string s = @"
entity Person {
    prop job: Job
}
enum Job { None,  Smith }
enum A { None, Farmer,}

event create {
    create Person $p
    set job = A.Farmer
}
";
        var db = Run(s, out var errors, 1);
        db.RunAction("create");
        db.Printer.PrintDb();
        var e = db.Entities.Single();
        PropertyId jobProp = db.GetPropertyId("Person","job");
        var value = e.GetProperty(jobProp);
    }
    [Test]
    public void TypeDeclarationBeforeEnumDefinition()
    {
        string s = @"
entity Person {
    prop job: Job
}
enum Job { X }

";
        
        var db = Run(s, out var errors);
        var p = db.GetPropertyId("Person", "job");
        db.GetPropertyType(p, out var t);
        Assert.That(t.BaseType, Is.EqualTo(PropertyValue.ValueBaseType.Enum));

    }

    [Test]
    public void TypeDeclarationBeforeUsedTypeReference()
    {
        string s = @"
entity Person {
    prop job: Job
}
entity Job { }

";
        
        var db = Run(s, out var errors);
        var p = db.GetPropertyId("Person", "job");
        db.GetPropertyType(p, out var t);
        Assert.That(t.BaseType, Is.EqualTo(PropertyValue.ValueBaseType.Ref));
        Assert.That(t, Is.EqualTo(db.GetEntityType("Job").RefType));

    }


    [Test]
    public void TypeQuery()
    {
        var s = @"
entity Person {
    prop alive: bool
}
event r {
    pick Person $p
    set $p.alive = true
}
";

        var db = Run(s, out var errors);

        EntityTypeId typePerson = db.GetEntityType("Person").Id;
        db.AllocateEntity(typePerson, "A");
        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];

        PropertyId propId = db.GetPropertyId("Person","alive");
        Assert.IsTrue(propId.IsValid);
        db.RunAction(action.Name);
        db.Printer.PrintDb();
        db.Commit();
        Assert.AreEqual(1, db.Entities.Count());
        Assert.AreEqual(true, db.Entities.Single().GetProperty(propId).BoolValue);
    }

}

public class NestedPropertyPathTests : TestsBase
{
    [Test]
    public void PropertyPath_Nested()
    {
        string s = @"
entity Person {
    prop x: number
    prop link: Person
}

event create {
    create Person $p
    create Person $p2
    set $p.link = $p2
    set $p2.x = 33
    assert_eq(33, $p.link.x)
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.Printer.PrintDb();
    }
    [Test]
    public void PropertyPath_Nested_Set()
    {
        string s = @"
entity Person {
    prop x: number
    prop link: Person
}

event create {
    create Person $p
    create Person $p2
    set $p.link = $p2
    set $p.link.x = 33
    assert_eq(33, $p.link.x)
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.Printer.PrintDb();
    }
    [Test]
    public void PropertyPath_Nested_Pick()
    {
        string s = @"
entity Person {
    prop x: number
    prop link: Person
}

event create {
    create Person $p2
    create Person $p
    set $p.link = $p2
    set $p2.x = 33
    pick Person $r: ($r.link.x = 33)
    set $r.x = 12
}
event check {
    pick Person $r: (link)
    assert_eq($r.x, 12)
    
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.Printer.PrintDb();
        db.RunAction("check");
    }
}
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
entity Person {
    prop f: number
}
event r {
    create Person $p
    set $p.f = 42
    assert $p.f = 42
}
event rr {
    pick Person $p
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
    public void Int_FromEnum()
    {
        var s = @"
entity Person {
    prop f: number
}
enum E { A, B, C }
event r {
    create Person $p
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
entity Person {
    prop f: bool
}
event r {
    create Person $p
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
entity Person {
    prop f: number
}
event r {
    create Person $p
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
entity Person {
    prop alive: bool
}
event born_char {
    create Person $p: ('{random Name}')
    set $p.alive = true
    assert($p.alive = true)
}
";

        var db = Run(s, out var errors);

        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];
        Assert.AreEqual("born_char", action.Name);
        Assert.AreEqual(3, action.Effects.Count);

        Assert.IsInstanceOf<SetProperty>(action.Effects[1]);


        PropertyId propId = db.GetPropertyId("Person","alive");
        Assert.IsTrue(propId.IsValid);
        db.RunAction(action.Name);
        db.Printer.PrintDb();
        db.Commit();
        Assert.AreEqual(1, db.Entities.Count());
    }

    [Test]
    public void FilterByType()
    {
        var s = @"
entity Person {
    prop alive: number
}
event born_char {
    create Person $p: ('{random Name}')
}
@start event init {
    call (born_char, 10)
}
event r {
    each Person $p {
        set $p.alive = 2
    }
}
";

        var db = Run(s, out var errors);

        // db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[2]);
        db.Printer.PrintDb();
        db.Commit();
        Assert.AreEqual(10, db.Entities.Count());
        Assert.AreEqual(2, db.Entities.Last().GetProperty(db.GetPropertyId("Person","alive")).IntValue);
    }

    [Test]
    public void Test2()
    {
        var s = @"
entity Person {
    prop alive: bool
}
event char_dies {
    pick Person $p: (alive = true)
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
entity E {
    prop alive: bool
}
event char_dies {
    pick E $x: (alive = true)
    pick E $y: (id != $x)
}";

        var db = Run(s, out var errors);

        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];
        Assert.AreEqual("char_dies", action.Name);
        Assert.AreEqual(2, action.Effects.Count);
    }

    [Test]
    public void Each_ExplicitPredicateVar()
    {
        var s = @"
entity Person {
    prop test: bool
}
event foreach {
    create Person $p
    each Person $x: ($x.test = false) {
        set $x.test = true
        record('{$x.name} {$x.test}')
    }
}";
        var db = Run(s, out var errors);
        db.RunAction("foreach");
    }

    [Test]
    public void Each()
    {
        var s = @"
entity Person {
    prop test: bool
}
event foreach {
    each Person $x {
        set $x.test = true
        record('{$x.name} {$x.test}')
    }
}";
        var db = Run(s, out var errors);
        db.History = new();
        var propId = db.GetPropertyId("Person","test");
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
entity Person {
    prop x: bool
    prop y: bool
}
event foreach {
    each Person $x {
        set $x.x = true
        record('{$x.name} {$x.x}')
    }
    each Person $x {
        set $x.y = true
        record('{$x.name} {$x.y}')
    }
}";
        var db = Run(s, out var errors, src => Assert.That(src.Contains("$1") || src.Contains("$2"), Is.False));
        db.History = new();
        var propX = db.GetPropertyId("Person","x");
        var propY = db.GetPropertyId("Person","y");
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
            Assert.IsTrue(entity.TryGetProperty(propY, out val));
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
entity Person {
    prop x: bool
    prop y: bool
}
event foreach {
    each Person $p {
        set $p.x = true
        record('{$p.name} {$p.x}')
    }
    each Person $p {
        set $p.y = true
        record('{$p.name} {$p.y}')
    }
}";
        var db = Run(s, out var errors, src => Assert.That(src.Contains("$1") || src.Contains("$2"), Is.False));
        db.History = new();
        var propX = db.GetPropertyId("Person","x");
        var propY = db.GetPropertyId("Person","y");
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
            Assert.IsTrue(entity.TryGetProperty(propY, out val));
            Assert.AreEqual(true, val.BoolValue);
        }

        foreach (var changeset in db.History.Changesets)
        {
            db.Printer.PrintChangeset(changeset);
        }
    }

    [Test]
    public void DuplicateVarNoError()
    {
        var s = @"
entity Person {
    prop alive: bool
}
event char_dies {
    pick Person $p: (alive = true)
    pick Person $p: (alive = true)
    set $p.alive = false
}";

        var db = Run(s, out var errors, 0);
    }

   

    [Test]
    public void PropertyPath_Var()
    {
        string s = @"
entity Person {
    prop x: number
    prop link: Person
}

event create {
    create Person $p
    create Person $p2
    set $p.link = $p2
    set $p2.x = 33
    var $tmp: $p.link
    assert_eq(33, $tmp.x)
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
entity Person {
    prop job: Job
}
enum Job { None, Farmer, Smith }

event create {
    create Person $p
    set $p.job = Job.Farmer
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.Printer.PrintDb();
        var e = db.Entities.Single();
        PropertyId jobProp = db.GetPropertyId("Person","job");
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
enum Job { None, Farmer, Smith }
entity Person {
    prop job: Job
}

event create {
    create Person $p
    set $p.job = 1
}
";
        var db = Run(s, out var errors);
        db.RunAction("create");
        db.Printer.PrintDb();
        var e = db.Entities.Single();
        PropertyId jobProp = db.GetPropertyId("Person","job");
        var value = e.GetProperty(jobProp);
        Assert.IsTrue(db.GetEnumDefinition("Job", out var enumDefinition));
        Assert.AreEqual(enumDefinition.ValueType, value.Type);
        Assert.AreEqual(PropertyValue.ValueBaseType.Enum, value.Type.BaseType);
        Assert.AreEqual(1, value.IntValue);
    }


    [Test]
    public void AssignRandomEnum()
    {
        string s = @"
entity Person {
    prop job: Job
}
enum Job { Farmer, Smith, Mayor }

event create {
    create Person $p
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
        PropertyId jobProp = db.GetPropertyId("Person","job");
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
entity Person {
prop faction: Faction
}
entity Faction {
prop owner: Person
}
event create_faction {
    pick Person $p: (faction = null)
    create Faction $f
    set $f.owner = $p
    set $p.faction = $f
}";
        var db = Run(s, out var errors);
        Assert.AreEqual(4, db.Actions[0].Effects.Count);
    }

    [Test]
    public void FormatLink()
    {
        var s = @"
entity E {
}

event e {
    create E $e
    record('{link($e, 'a link')} here')

}";
        var db = Run(s, out _);
        db.History = new();
        db.RunAction(db.Actions[0]);
        Console.WriteLine(db.Records.Last().Text);
        Assert.That(db.Records.Last().Text, Is.EqualTo("<#1>a link</> here"));
    }

    [Test]
    public void Format()
    {
        var s = @"
entity Person {
}
entity Faction {
    prop owner: Person
}
event create_faction {
    create Faction $f: ('Faction of {random Name}')
    create Faction $g
    set $g.name = 'Circle of {random Name}'
    create Person $p
    set $p.name = '{random Name}'
    set $f.owner = $p
    record('{$p.name} creates the {$f.name} to counter the {$g.name}')
    assert_eq('{$p.name} creates the {$f.name} to counter the {$g.name}', 'River creates the Faction of Cerelia to counter the Circle of Hecate')
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        Console.WriteLine(db.Records.Last().Text);
        Assert.That(db.Records.Last().Text, Is.EqualTo("<#3>River</> creates the <#1>Faction of Cerelia</> to counter the <#2>Circle of Hecate</>"));
    }

    [Test]
    public void SQL_DefaultTypeProp()
    {
        Run(@"
entity Person {}
@frequency(1, EveryXYear, 1)
event youngs_grow {
    each Person $p: (type = Person){
        record ''
    }
}", out _).RunAction("youngs_grow");
    }

    [Test]
    public void Format_TwoRandomNames()
    {
        var s = @"
entity Person {
}
event create_faction {
    create Person $p: ('{random Name}-{random Name} of {random Name}')
    assert_eq($p.name, 'Cerelia-Hecate of River')
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
    }

    [Test]
    public void FormatLiteral()
    {
        var s = @"
event create_faction {
    record('res {42} > {16} = {42 > 16}')
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
event called {
    create E $e
}
event call {
    call called
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[1]);
        Assert.AreEqual(1, db.Entities.Count());
    }

    [Test, Ignore("calls return values aren' t typed yet")]
    public void CallRuleReturnValue()
    {
        var s = @"
entity E {
    prop x: number
}
event called {
    create E $x
}
event call {
    var $x: call called
    var $y: call called
    
    set $y.x = 42
    assert_eq ($x, 1)
    assert_eq ($y, 2)
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
entity E {
    prop x: number
}

event call {
    var $w:  42
    var $g: 43
    assert_eq($w, 42)
    assert_eq($g, 43)
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
event create {
    create Time $t: 'time'
    set year = 1000
}
event read {
    assert_eq(#Time.year, 1000)
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

event init {
    create Time $t: 'time'
    set $t.year = 345
}
@frequency(4, PerXYear, 1)
event born {
    create Person $p
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
enum Age { Child, Young, Adult, Old }
entity Person {
    prop alive: bool
    prop birthdate: number
    prop age: Age
}
event create_time {
    create Time $t: 'time'
    set $t.year = 1000
}
event born {
    create Person $p
    set $p.alive = true
    set $p.age = Age.Child
    set $p.birthdate = #Time.year
    record('The {$p.age} {$p.name} is born in {$p.birthdate}')
}

event pass_15_years {
    set #Time.year = #Time.year + 15
    each Person $p: (alive = true, age = Age.Child, (birthdate + 20) <= #Time.year) {
        set $p.age = Age.Young
    }
    each Person $p: (alive = true, age = Age.Young, (birthdate + 40) <= #Time.year) {
        set $p.age = Age.Adult
    }
    each Person $p: (alive = true, age = Age.Adult, (birthdate + 60) <= #Time.year) {
        set $p.age = Age.Old
    }
    each Person $p: (alive = true, age = Age.Old, (birthdate + 80) <= #Time.year) {
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
entity Person {
prop birthdate: number
}
event born {
    pick Time $t
    create Person $p
    set $p.birthdate = $t.year
}";
        Run(s, out _);
    }

    [Test]
    public void SetVar2XEqVar1Y_ImplicitVar()
    {
        string s = @"
entity Person {
    prop birthdate: number
}
event born {
    pick Time $t
    create Person $p
    set birthdate = $t.year
}";
        Run(s, out _);
    }

    [Test]
    public void Time2()
    {
        var s = @"
enum Age { Child, Young, Adult, Old }
entity Person {
    prop alive: bool
    prop birthdate: number
    prop age: Age
}
event create_time {
    create Time $t: 'time'
    set $t.year = 1000
}
event born {
    pick Time $t
    create Person $p
    set $p.alive = true
    set $p.age = Age.Child
    set $p.birthdate = $t.year
    record('The {$p.age} {$p.name} is born in {$p.birthdate}')
}

event pass_15_years {
    pick Time $t
    set $t.year = $t.year + 15
    each Person $p: (alive = true, age < Age.Old, (birthdate + 20* (age+1) ) <= $t.year, age < Age.Old ){
        set $p.age = age+1
    }
   
    each Person $p: (alive = true, age = Age.Old, (birthdate + 80) <= $t.year){
        set $p.alive = false
    }
}";
        var db = Run(s, out var errors);
        db.History = new();
        db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[1]);
        db.Printer.PrintDb();
        db.Commit();
        Assert.AreEqual(1000, db.Entities.Last().GetProperty(db.GetPropertyId("Person","birthdate")).IntValue);
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
        db.Printer.PrintHistory();
    }

    [Test]
    public void TestAssertFalse()
    {
        var s = @"
event r {
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
event r {
    assert(1 = 2)
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
event r {
    assert(true)
}";
        var db = Run(s, out var errors);
        db.History = new();

        NoAssertInstruction(() => db.RunAction(db.Actions[0]));
        db.Printer.PrintDb();
    }
}
