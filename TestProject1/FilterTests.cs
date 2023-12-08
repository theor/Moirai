namespace TestProject1;

public class MarkTests : TestsBase
{
    [Test]
    public void MarkCurrentEvent()
    {
        var s = @"
entity Person {
prop x: number
}
@start
event create {
    create Time $t
}
event e {
    create Person $p
    mark($p)
}";

        var db = Run(s, out _);
        db.Ctx.PassYears(10, true);
        db.RunAction("e");
        db.Printer.PrintMarked();
        db.Ctx.GetLastMarked(db.Entities.Last().Id, db.Actions.Last().Id, out var year);
        Assert.That(year, Is.EqualTo(10l));
    }
    [Test]
    public void MarkCurrentEventAndQuery()
    {
        var s = @"
entity Person {
prop x: number
}
@start
event e {
    create Person $p
    create Time $t
}
event since {
    pick Person $p
    var $since: since_last($p)
    mark($p)
    record('since last: {$since}')
}
";

        var db = Run(s, out _);
        db.History = new();
        db.Ctx.PassYears(10, true);
        db.RunAction("since");
        db.Printer.PrintMarked();
        db.Printer.PrintHistory();
        db.Ctx.PassYears(15, true);
        db.RunAction("since");
        db.Printer.PrintMarked();
        db.Printer.PrintRecords();
        Assert.That(db.Records[0].Text, Is.EqualTo("since last: " +int.MinValue));
        Assert.That(db.Records[1].Text, Is.EqualTo("since last: 15"));
    }
    
    // TODO pick $x: (since_last($x) > 4) should not compile
}
public class FilterTests : TestsBase
{
    [Test]
    public void Filter_Start()
    {
        var s = @"
entity Person {
    prop alive: bool
}
@start
event char_dies {
    pick Person $p: (alive = true)
    set $p.alive = false
}";

        Run(s, out _);
    }


    [Test]
    public void Filter_Every()
    {
        var s = @"
entity Person {
    prop alive: bool
}
@1 every 1 year
event char_dies {
    pick Person $p: (alive = true)
    set $p.alive = false
}";

        Run(s, out _);
    }

    [Test]
    public void Filter_Frequency()
    {
        var s = @"
entity Person {
    prop alive: bool
}
@1 per 2 years
event char_dies {
    pick Person $p: (alive = true)
    set $p.alive = false
}";

        Run(s, out _);
    }
}
