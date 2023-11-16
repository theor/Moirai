namespace TestProject1;

public class MatchTests : TestsBase
{
    [Test]
    public void MatchAnyValue()
    {
        var db = Run(@"
entity T {}
prop p: number
rule r {
    create $t: T
    match 2 {
        _ => set $t.p = 20
        2 => set $t.p = 30
    }
}", out _);
        db.RunAction("r");
        Assert.AreEqual(20, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }

    [Test]
    public void MatchOneValue()
    {
        var db = Run(@"
entity T {}
prop p: number
rule r {
    create $t: T
    match 2 {
        1 => set $t.p = 10
        2 => set $t.p = 20
        3 => set $t.p = 30
    }
}", out _);
        db.RunAction("r");
        Assert.AreEqual(20, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }

    [Test]
    public void MatchTwoValue()
    {
        var db = Run(@"
entity T {}
prop p: number
rule r {
    create $t: T
    match 2,(3>4) {
        1,true => set $t.p = 10
        2,true => set $t.p = 20
        2,false => set $t.p = 30
    }
}", out _);
        db.RunAction("r");
        Assert.AreEqual(30, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }

    [Test]
    public void RandomWeighted()
    {
        var db = Run(@"
entity T {}
prop x: number
prop y: number
prop z: number
@start
rule create {
    create $t: T
}
rule r {
    random_weighted 10 {
        1 => set #T.x = #T.x + 1
        4 => set #T.y = #T.y + 1
        5 => set #T.z = #T.z + 1
    }
}", out _);
        for (int i = 0; i < 1000; i++)
        {
            db.RunAction("r");
        }

        db.Printer.PrintDb();
        // Assert.AreEqual(20, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }

    [Test]
    public void RandomWeighted_AnyValue()
    {
        var db = Run(@"
entity T {}
prop x: number
prop y: number
prop z: number
@start
rule create {
    create $t: T
}
rule r {
    random_weighted 10 {
        1 => set #T.x = #T.x + 1
        4 => set #T.y = #T.y + 1
        _ => set #T.z = #T.z + 1
    }
}", out _);
        for (int i = 0; i < 1000; i++)
        {
            db.RunAction("r");
        }

        db.Printer.PrintDb();
        // Assert.AreEqual(20, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }

    [Test]
    public void RandomWeighted_Create()
    {
        var db = Run(@"
entity Asteroid {}
entity Star {}
entity Planet {}
entity Satelite {}
rule create {
    random_weighted 10 {
        1 => create $x: Star
        6 => create $x: Asteroid
        2 => create $x: Planet
        1 => create $x: Satelite
    }
}", out _);
        for (int i = 0; i < 100; i++)
        {
            db.RunAction("create");
        }

        db.Printer.PrintDb();
        Console.WriteLine(String.Join("\n", db.Entities.GroupBy(e => e.Type).Select(g => (db.GetEntityTypeName(g.Key), g.Count()))));
        // Assert.AreEqual(20, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }

    [Test]
    public void RandomWeighted_Enum()
    {
        var db = Run(@"
entity Person {}
enum Job {
    Farmer,
    Smith,
    Soldier,
    King,
}
prop job: Job
rule create {
    create $p: Person
    random_weighted 10 {
        6 => set job = Job.Farmer
        1 => set job = Job.Smith
        _ => set job = Job.Soldier
    }
}", out _);
        for (int i = 0; i < 100; i++)
        {
            db.RunAction("create");
        }

        db.Printer.PrintDb();
        var p = db.GetPropertyId("job");
        Console.WriteLine(String.Join("\n", db.Entities.GroupBy(e => e.GetProperty(p)).Select(g => (db.Printer.Print(g.Key), g.Count()))));
        // Assert.AreEqual(20, db.Entities.Single().GetProperty(db.GetPropertyId("p")).IntValue);
    }
}
