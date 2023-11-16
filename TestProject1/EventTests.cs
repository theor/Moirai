namespace TestProject1;

public class EventTests : TestsBase
{
    
    [Test]
    public void Event()
    {
        var s = @"
entity Person {}
prop alive: bool
prop test: bool
rule born {
    create $p: Person
    set alive = true
}

rule die {
    each $p: type=Person, alive = true {
        set alive = false
        record '{$p} dies'
    }
}

event on_death {
    when $new.alive = false

    set test = true
    record 'event on {$new}'
}";
        var db = Run(s, out _, 0);
        db.History = new();
        Assert.AreEqual(1, db.Events.Count);

        db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[1]);
        db.Printer.PrintDb();
        Entity e = db.Entities.First();
        PropertyId propTest = db.GetPropertyId("test");
        Assert.AreEqual(true, e.GetProperty(propTest).BoolValue);
        foreach (var historyChangeset in db.History.Changesets)
        {
            Console.WriteLine(historyChangeset.ActionName);
            db.Printer.PrintChangeset(historyChangeset);
        }
    }
    [Test]
    public void EventCompareOldNewValues()
    {
        var s = @"
entity Person {}
prop x: number
prop test: number
rule born {
    create $p: Person
    set x = 1
    set test = 1
}

rule die {
    each $p: type=Person, x = 1 {
        set x = 2
        record '{$p} dies'
    }
}

event on_death {
    when $new.x = 2, $old.x = 1

    set test = 10
    record 'event on {$new}'
}
event on_death2 {
    when $new.x = 2, $old.x = 3

    set test = 20
    record 'event on {$new}'
}";
        var db = Run(s, out _, 0);
        db.History = new();

        db.RunAction(db.Actions[0]);
        // db.RunAction(db.Actions[0]);
        db.RunAction(db.Actions[1]);
        db.Printer.PrintDb();
        Entity e = db.Entities.First();
        PropertyId propTest = db.GetPropertyId("test");
        Assert.That(e.GetProperty(propTest).IntValue, Is.EqualTo(10));
        foreach (var historyChangeset in db.History.Changesets)
        {
            Console.WriteLine(historyChangeset.ActionName);
            db.Printer.PrintChangeset(historyChangeset);
        }
    }

    [Test]
    public void Event2()
    {
        var s = @"
entity Person {}
entity Item {}
entity Link {}
prop alive: bool
prop child: Person
prop parent: Person
prop owner: ref

event inherit {
    when $new.type = Person, $new.alive = false
    each $i: type = Item, owner = $new {
        pick $l: type = Link, $l.parent = $new 
        pick $c: type = Person, alive = true, id = $l.child
            set $i.owner = $c
            record ""{$c.name} inherits the {$i.name} from {$new.name}""
    }
}";
        var db = Run(s, out _, 0);
        Assert.AreEqual(1, db.Events.Count);

        // db.RunAction(db.Events[0]);
        db.Printer.PrintDb();
        // Entity e = db.Entities.Single();
        // PropertyId propTest = db.GetProperty("test");
        // Assert.AreEqual(true, e.GetProperty(propTest).BoolValue);
    }


    [Test, Ignore("obsolete json...")]
    public void Bug_Inherit()
    {
        string json =
            "[{\"Id\":1,\"Properties\":[{\"Type\":2,\"Value\":{\"Value\":null,\"IntValue\":1,\"Type\":[6,0]}},{\"Type\":3,\"Value\":{\"Value\":\"time\",\"IntValue\":-2147483648,\"Type\":[1,0]}},{\"Type\":4,\"Value\":{\"Value\":null,\"IntValue\":844,\"Type\":[3,0]}}]},{\"Id\":2,\"Properties\":[{\"Type\":2,\"Value\":{\"Value\":null,\"IntValue\":2,\"Type\":[6,0]}},{\"Type\":3,\"Value\":{\"Value\":\"Lowenna Tarian\",\"IntValue\":-2147483648,\"Type\":[1,0]}},{\"Type\":6,\"Value\":{\"Value\":\"Lowenna\",\"IntValue\":-2147483648,\"Type\":[1,0]}},{\"Type\":7,\"Value\":{\"Value\":null,\"IntValue\":1,\"Type\":[4,0]}},{\"Type\":5,\"Value\":{\"Value\":null,\"IntValue\":3,\"Type\":[5,2]}},{\"Type\":8,\"Value\":{\"Value\":null,\"IntValue\":764,\"Type\":[3,0]}},{\"Type\":11,\"Value\":{\"Value\":null,\"IntValue\":2,\"Type\":[5,1]}},{\"Type\":9,\"Value\":{\"Value\":null,\"IntValue\":3,\"Type\":[2,0]}}]},{\"Id\":3,\"Properties\":[{\"Type\":2,\"Value\":{\"Value\":null,\"IntValue\":2,\"Type\":[6,0]}},{\"Type\":3,\"Value\":{\"Value\":\"Aeron Morgaine\",\"IntValue\":-2147483648,\"Type\":[1,0]}},{\"Type\":6,\"Value\":{\"Value\":\"Aeron\",\"IntValue\":-2147483648,\"Type\":[1,0]}},{\"Type\":7,\"Value\":{\"Value\":null,\"IntValue\":1,\"Type\":[4,0]}},{\"Type\":5,\"Value\":{\"Value\":null,\"IntValue\":1,\"Type\":[5,2]}},{\"Type\":8,\"Value\":{\"Value\":null,\"IntValue\":784,\"Type\":[3,0]}},{\"Type\":11,\"Value\":{\"Value\":null,\"IntValue\":2,\"Type\":[5,1]}},{\"Type\":9,\"Value\":{\"Value\":null,\"IntValue\":2,\"Type\":[2,0]}}]},{\"Id\":4,\"Properties\":[{\"Type\":2,\"Value\":{\"Value\":null,\"IntValue\":3,\"Type\":[6,0]}},{\"Type\":3,\"Value\":{\"Value\":\"ring of Lowenna Tarian\",\"IntValue\":-2147483648,\"Type\":[1,0]}}]},{\"Id\":5,\"Properties\":[{\"Type\":2,\"Value\":{\"Value\":null,\"IntValue\":3,\"Type\":[6,0]}},{\"Type\":3,\"Value\":{\"Value\":\"Portrait of Aeron Morgaine\",\"IntValue\":-2147483648,\"Type\":[1,0]}},{\"Type\":12,\"Value\":{\"Value\":null,\"IntValue\":1,\"Type\":[5,3]}},{\"Type\":15,\"Value\":{\"Value\":null,\"IntValue\":2,\"Type\":[2,0]}}]},{\"Id\":6,\"Properties\":[{\"Type\":2,\"Value\":{\"Value\":null,\"IntValue\":3,\"Type\":[6,0]}},{\"Type\":3,\"Value\":{\"Value\":\"Portrait of Aeron Morgaine\",\"IntValue\":-2147483648,\"Type\":[1,0]}},{\"Type\":12,\"Value\":{\"Value\":null,\"IntValue\":1,\"Type\":[5,3]}},{\"Type\":15,\"Value\":{\"Value\":null,\"IntValue\":2,\"Type\":[2,0]}}]},{\"Id\":7,\"Properties\":[{\"Type\":2,\"Value\":{\"Value\":null,\"IntValue\":2,\"Type\":[6,0]}},{\"Type\":3,\"Value\":{\"Value\":\"Auberon Lowennason\",\"IntValue\":-2147483648,\"Type\":[1,0]}},{\"Type\":6,\"Value\":{\"Value\":\"Auberon\",\"IntValue\":-2147483648,\"Type\":[1,0]}},{\"Type\":7,\"Value\":{\"Value\":null,\"IntValue\":1,\"Type\":[4,0]}},{\"Type\":5,\"Value\":{\"Value\":null,\"IntValue\":0,\"Type\":[5,2]}},{\"Type\":8,\"Value\":{\"Value\":null,\"IntValue\":804,\"Type\":[3,0]}}]},{\"Id\":8,\"Properties\":[{\"Type\":2,\"Value\":{\"Value\":null,\"IntValue\":5,\"Type\":[6,0]}},{\"Type\":13,\"Value\":{\"Value\":null,\"IntValue\":2,\"Type\":[2,0]}},{\"Type\":14,\"Value\":{\"Value\":null,\"IntValue\":7,\"Type\":[2,0]}}]},{\"Id\":9,\"Properties\":[{\"Type\":2,\"Value\":{\"Value\":null,\"IntValue\":5,\"Type\":[6,0]}},{\"Type\":13,\"Value\":{\"Value\":null,\"IntValue\":3,\"Type\":[2,0]}},{\"Type\":14,\"Value\":{\"Value\":null,\"IntValue\":7,\"Type\":[2,0]}}]}]";
        string script = @"
entity Person {}
entity Item {}
entity Faction {}
entity Link {}

tag #death

enum Job { Smith, Farmer, Painter, Sculptor }
enum Age { Child, Young, Adult, Old }
enum ItemType { Forged, Painted, Sculpted }

prop age: Age
prop first_name: string
prop alive: bool
prop birthdate: number
prop partner: ref
prop faction: ref
prop job: Job
prop item_type: ItemType

prop parent: ref
prop child: ref

prop owner: ref

event inherit {
    when #death
    when $p: type = Person, alive = false
    record '## {$p} {$p.name} died, inheriting'
    each $i: type = Item, owner = $p {
        record '##   {$i} {$i.name} item'
        pick $l: type = Link, $l.parent = $p
        set $i.owner = $l.child
        var $c = $l.child
        record '{$c.name} inherits the {$i.name} from {$p.name} - {$l}'
    }
}

@1 every 1 year
rule olds_dies {
    each $p: type=Person, alive = true, age = Age.Old, (birthdate + 80) <= #Time.year{
        set $p.alive = false
        record '{$p.name} dies of old age at {#Time.year - $p.birthdate} in {#Time.year}'
    }
}
";

        var db = StoryParser.Parse(script, out var error);
        db.Deserialize(json);
        db.Init();
        db.Printer.PrintDb();
        db.Ctx.PassYears(1, true);
        // db.PrintDb();
        db.Printer.PrintChangeset(db.CurrentChangeset, false);
        Console.WriteLine(db.Records.Last().Text);
    }

}
