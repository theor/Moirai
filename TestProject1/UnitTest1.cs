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
        Assert.AreEqual(errorCount, errors.Count, string.Join(", ", errors));
        if (errorCount == 0)
        {
            var reparsed = StoryParser.Parse(printed, out var errors2);
            Assert.AreEqual(errorCount, errors2.Count, "During reparse: " + string.Join(", ", errors2));
            Console.WriteLine("### REPRINT 2");
            Console.WriteLine(reparsed.Printer.Print());
        }
        return db;
    }

    [Test]
    public void Test1()
    {
        var s = @"
entity person {}
prop alive = bool
rule born_char {
    create person
    set alive = true
}
";
        
        var db = Run(s, out var errors);
        
        Assert.AreEqual(1, db.Actions.Count);
        var action = db.Actions[0];
        Assert.AreEqual("born_char", action.Name);
        Assert.AreEqual(2, action.Effects.Count);
        
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
    public void Test2()
    {
        var s = @"
entity person {}
prop alive = bool
rule char_dies {
    pick $p: type = ""person"", alive = true
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
entity person {}
prop test = bool
rule foreach {
    each $x: type = ""person"" {
        set $x.test = true
        format ""{$x.name} {$x.test}""
    }
}";
        var db  =Run(s, out var errors);
        db.History = new();
        var propId = db.GetProperty("test");
        var typePerson = db.GetEntityType("person");
        db.AllocateEntity(typePerson, "A");
        db.AllocateEntity(typePerson, "B");
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
entity person {}
prop alive = bool
prop test = bool
rule born {
    create ""person""
    set alive = true
}

rule die {
    pick type = ""person"", alive = true
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
    public void Test3()
    {
        var s = @"
prop alive = bool
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
        
        Assert.NotNull(pe1.Predicate);
        Assert.IsInstanceOf<PropertyOperator>(pe1.Predicate);
        Assert.NotNull(pe2.Predicate);
        Assert.IsInstanceOf<PropertyOperator>(pe2.Predicate);

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
    $x = pick(type = ""person"", alive = true, partner = null)
    $y = pick(type = ""person"", alive = true, partner = null, id != $x)
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
        
        Assert.NotNull(pe1.Predicate);
        Assert.IsInstanceOf<And>(pe1.Predicate);
        Assert.NotNull(pe2.Predicate);
        Assert.IsInstanceOf<And>(pe2.Predicate);

    }


    [Test]
    public void ParseWholeFile()
    {
        var path = Path.GetFullPath("../../../../MoiraiCli/w.sg");
        Console.WriteLine(path);
        Assert.IsTrue(File.Exists(path));
        var db = StoryParser.Parse(File.ReadAllText(path), out var errors);
        Console.WriteLine("------------------");
        Console.WriteLine(db.Printer.Print());
        Assert.AreEqual(0, errors.Count, string.Join(", ", errors));
 
    }
    
    [Test]
    public void DuplicateVarError()
    {
        var s = @"
prop alive = bool
rule char_dies {
    $p = pick type = ""person"", alive = true
    $p = pick type = ""person"", alive = true
    set $p.alive = false
}";
       
        var db = Run(s, out var errors, 1);
       
    }
    [Test]
    public void Prop_Enum()
    {
        string s = @"
entity person {}
enum Job = None, Farmer, Smith
prop job = Job

rule create {
    create ""person""
    set job = ""Farmer""
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
entity person {}
enum Job = None, Farmer, Smith
prop job = Job

rule create {
    create ""person""
    set job = ""asd""
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
entity person {}
enum Job = None, Farmer, Smith
prop job = Job

rule create {
    create ""person""
    set job = random
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
    public void AssignCreate()
    {
        var s = @"
entity person {}
entity faction {}
prop faction = ref
prop owner = ref
rule create_faction {
    pick $p: type=""person"", faction = null
    create $f: ""faction""
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
entity person {}
entity faction {}
prop owner = ref
rule create_faction {
    create $f: ""faction""
    create $g: ""faction""
    create $p: ""person""
    set $f.owner = $p
    format ""{$p.name} creates the {$f.name} to counter the {$g.name}""
}";
        var db = Run(s, out var errors);
        Assert.AreEqual(5, db.Actions[0].Effects.Count);
        db.History = new();
        db.RunAction(db.Actions[0]);
        db.Printer.PrintChangeset(db.History.Changesets[0], false);
        Console.WriteLine(db.History.Changesets[0].Description);
        Assert.AreEqual("River creates the Faction of Cerelia to counter the Faction of Hecate", db.History.Changesets[0].Description);
    }
}