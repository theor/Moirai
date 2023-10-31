namespace TestProject1;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    public static List<Action> Run(string s, out List<StoryParser.Error> errors, int errorCount = 0)
    {
        Console.WriteLine(s);
        var actions = StoryParser.Parse(s, out errors);
        var printed = StoryPrinter.Print(actions);
        Console.WriteLine(printed);
        Assert.AreEqual(errorCount, errors.Count, string.Join(", ", errors));
        if (errorCount == 0)
        {
            var reparsed = StoryParser.Parse(printed, out var errors2);
            Assert.AreEqual(errorCount, errors2.Count, string.Join(", ", errors2));
            Console.WriteLine(StoryPrinter.Print(reparsed));
        }
        return actions;
    }

    [Test]
    public void Test1()
    {
        var s = @"
@born_char
    create person
    set alive = true
";
        
        var actions = Run(s, out var errors);
        
        Assert.AreEqual(1, actions.Count);
        var action = actions[0];
        Assert.AreEqual("born_char", action.Name);
        Assert.AreEqual(2, action.Effects.Count);
        
        Assert.IsInstanceOf<SetProperty>(action.Effects[1]);
        Assert.AreEqual(PropertyType.Alive, ((SetProperty)action.Effects[1]).PropertySet.Property);

        Database db = new Database { Effects = actions };
        db.RunAction(action.Name);
        db.PrintDb();
        Assert.AreEqual(1, db.Entities.Count());
    }
    [Test]
    public void Test2()
    {
        var s = @"
@char_dies
    $p = pick type = ""person"", alive = true
    set $p.alive = false
";
        var actions = Run(s, out var errors);
        
        Assert.AreEqual(1, actions.Count);
        var action = actions[0];
        Assert.AreEqual("char_dies", action.Name);
        Assert.AreEqual(2, action.Effects.Count);
    }
    [Test]
    public void Test3()
    {
        var s = @"
@char_dies
    $x = pick alive = true
    $y = pick id != $x
";
       
        var actions = Run(s, out var errors);
        
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
        var actions = StoryParser.Parse(s, out var errors);
        Console.WriteLine(StoryPrinter.Print(actions));
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
        var actions = StoryParser.Parse(s, out var errors);
        Console.WriteLine(StoryPrinter.Print(actions));
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
        var path = Path.GetFullPath("../../../../ConsoleApp1/w.sg");
        Console.WriteLine(path);
        Assert.IsTrue(File.Exists(path));
        var actions = StoryParser.Parse(File.ReadAllText(path), out var errors);
        Console.WriteLine("------------------");
        Console.WriteLine(StoryPrinter.Print(actions));
        Assert.AreEqual(0, errors.Count, string.Join(", ", errors));
 
    }
    
    [Test]
    public void DuplicateVarError()
    {
        var s = @"
@char_dies
    $p = pick type = ""person"", alive = true
    $p = pick type = ""person"", alive = true
    set $p.alive = false
";
       
        var actions = Run(s, out var errors, 1);
       
    }
    [Test]
    public void AssignCreate()
    {
        var s = @"
@create_faction
    $p = pick type=""person"", faction = null
    $f = create ""faction""
    set $f.owner = $p
    set $p.faction = $f
";
        var actions = Run(s, out var errors);
        Assert.AreEqual(4, actions[0].Effects.Count);
       
    }
    [Test]
    public void Format()
    {
        var s = @"
@create_faction
    $f = create ""faction""
    $g = create ""faction""
    $p = create ""person""
    set $f.owner = $p
    format ""{$p.name} creates the {$f.name} to counter the {$g.name}""
";
        var actions = Run(s, out var errors);
        Assert.AreEqual(4, actions[0].Effects.Count);
        Database db = new Database { Effects = actions, History = new()};
        db.RunAction(actions[0]);
        StoryPrinter.PrintChangeset(db.History.Changesets[0], false);
        Console.WriteLine(db.History.Changesets[0].Description);
        Assert.AreEqual("River creates the Faction of Cerelia to counter the Faction of Hecate", db.History.Changesets[0].Description);
    }
}