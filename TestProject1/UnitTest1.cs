namespace TestProject1;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    public static (List<Action> actions, List<string> properties) Run(string s, out List<StoryParser.Error> errors, int errorCount = 0)
    {
        Console.WriteLine(s);
        var (actions, properties) = StoryParser.Parse(s, out errors);
        var printed = StoryPrinter.Print(actions, properties);
        Console.WriteLine("### REPRINT");
        Console.WriteLine(printed);
        Assert.AreEqual(errorCount, errors.Count, string.Join(", ", errors));
        if (errorCount == 0)
        {
            var reparsed = StoryParser.Parse(printed, out var errors2);
            Assert.AreEqual(errorCount, errors2.Count, "During reparse: " + string.Join(", ", errors2));
            Console.WriteLine("### REPRINT 2");
            Console.WriteLine(StoryPrinter.Print(reparsed.Item1, reparsed.Item2));
        }
        return (actions, properties);
    }

    [Test]
    public void Test1()
    {
        var s = @"
prop alive = bool
rule born_char {
    create person
    set alive = true
}
";
        
        var (actions, properties) = Run(s, out var errors);
        
        Assert.AreEqual(1, actions.Count);
        var action = actions[0];
        Assert.AreEqual("born_char", action.Name);
        Assert.AreEqual(2, action.Effects.Count);
        
        Assert.IsInstanceOf<SetProperty>(action.Effects[1]);
        

        Database db = new Database(properties, actions);
        PropertyId propId = db.GetProperty("Alive");
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
prop alive = bool
rule char_dies {
    pick $p: type = ""person"", alive = true
    set $p.alive = false
}";
        var (actions, props) = Run(s, out var errors);
        
        Assert.AreEqual(1, actions.Count);
        var action = actions[0];
        Assert.AreEqual("char_dies", action.Name);
        Assert.AreEqual(2, action.Effects.Count);
    }
    [Test]
    public void Each()
    {
        var s = @"
prop test = bool
rule foreach {
    each $x: type = ""person"" {
        set $x.test = true
        format ""{$x.name} {$x.test}""
    }
}";
        var (actions, properties) =Run(s, out var errors);
        var db = new Database(properties, actions){History = new()};
        var propId = db.GetProperty("test");
        db.AllocateEntity(EntityType.Person, "A");
        db.AllocateEntity(EntityType.Person, "B");
        db.PrintDb();
        foreach (var entity in db.Entities)
        {
            Assert.IsFalse(entity.TryGetProperty(propId, out var val));
        }
        db.RunAction(actions[0]);
        db.PrintDb();
        foreach (var entity in db.Entities)
        {
            Assert.IsTrue(entity.TryGetProperty(propId, out var val));
            Assert.AreEqual(true, val.BoolValue);
        }
        foreach (var changeset in db.History.Changesets)
        {
            StoryPrinter.PrintChangeset(changeset, db);
            Console.WriteLine(changeset.Description);
            
        }
    }
    [Test]
    public void Event()
    {
        var s = @"
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
        var (actions, p) = Run(s, out _, 0);
        Assert.AreEqual(1, actions.Count(a => a.IsEvent));

        var db = new Database(p, actions);
        db.RunAction(actions[0]);
        db.RunAction(actions[1]);
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
       
        var (actions, props) = Run(s, out var errors);
        
        Assert.AreEqual(1, actions.Count);
        var action = actions[0];
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
        var (actions, props) = StoryParser.Parse(s, out var errors);
        Console.WriteLine(StoryPrinter.Print(actions, props));
        Console.WriteLine(string.Join("\n", errors.Select(e => ToString())));
        Assert.AreEqual(0, errors.Count);
        
        Assert.AreEqual(1, actions.Count);
        var action = actions[0];
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
        var (actions, props) = StoryParser.Parse(s, out var errors);
        Console.WriteLine(StoryPrinter.Print(actions, props));
        Console.WriteLine(string.Join("\n", errors.Select(e => ToString())));
        Assert.AreEqual(0, errors.Count);
        
        Assert.AreEqual(1, actions.Count);
        var action = actions[0];
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
        var (actions, props) = StoryParser.Parse(File.ReadAllText(path), out var errors);
        Console.WriteLine("------------------");
        Console.WriteLine(StoryPrinter.Print(actions, props));
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
       
        var (actions, props) = Run(s, out var errors, 1);
       
    }
    [Test]
    public void AssignCreate()
    {
        var s = @"
prop faction = ref
prop owner = ref
rule create_faction {
    pick $p: type=""person"", faction = null
    create $f: ""faction""
    set $f.owner = $p
    set $p.faction = $f
}";
        var (actions, props) = Run(s, out var errors);
        Assert.AreEqual(4, actions[0].Effects.Count);
       
    }
    [Test]
    public void Format()
    {
        var s = @"
prop owner = ref
rule create_faction {
    create $f: ""faction""
    create $g: ""faction""
    create $p: ""person""
    set $f.owner = $p
    format ""{$p.name} creates the {$f.name} to counter the {$g.name}""
}";
        var (actions, props) = Run(s, out var errors);
        Assert.AreEqual(5, actions[0].Effects.Count);
        Database db = new Database(props, actions) { History = new()};
        db.RunAction(actions[0]);
        StoryPrinter.PrintChangeset(db.History.Changesets[0], db, false);
        Console.WriteLine(db.History.Changesets[0].Description);
        Assert.AreEqual("River creates the Faction of Cerelia to counter the Faction of Hecate", db.History.Changesets[0].Description);
    }
}