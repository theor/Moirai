internal class Program
{
    public static void Main(string[] args)
    {
        string line;
        string path = "w.sg";
        var db = new Database() { Effects = StoryParser.Parse(File.ReadAllText(path), out var errors) };
        Console.WriteLine(StoryPrinter.Print(db.Effects));
        int prevAction = -1;
        while (true)
        {
            Console.WriteLine("-----------------");
            for (var index = 0; index < db.Effects.Count; index++)
            {
                var action = db.Effects[index];
                Console.WriteLine($"  {index:00} {action.Name}");
            }
            Console.Write("> ");
            line = Console.ReadLine() ?? "";
            if (line == "qq")
                break;

            if (line == "" && prevAction >= 0)
            {
                db.RunAction(db.Effects[prevAction]);
                db.PrintDb();
            }
            if (int.TryParse(line, out var i) && i >= 0 && i < db.Effects.Count)
            {
                prevAction = i;
                db.RunAction(db.Effects[i]);
                db.PrintDb();
            }

        }
        // Console.WriteLine("Hello, World!");
        // var rules = new List<Rule>
        // {
        //     new Rule(new Property(PropertyType.Type, "Person"))
        // };

        // var effects = StoryParser.Parse(File.ReadAllText("w.sg"));
        // return;

        Sample();
        return;

        db.RunAction("Create person");
        db.RunAction("Create person");
        db.PrintDb();
        db.RunAction("Two people marry");
        db.PrintDb();
        db.RunAction("Two people separate");
        db.PrintDb();
        db.RunAction("Someone dies");
        db.PrintDb();
// return;
        Console.WriteLine("  [ITEMS]");
        db.RunAction("Create item");
        db.RunAction("Create item");
        db.PrintDb();
        db.RunAction("Set item owner");
        // PrintDb(db);
        db.PrintDb();
        db.RunAction("Set item owner");
        db.PrintDb();


    }
    private static void Sample()
    {

        var db = new Database
        {
            Rules =
            {
                new Rule("Persons have liveliness", new PropertyEquals(EntityType.Person),
                    new HasProperty(PropertyType.Alive)),
                new Rule("Items have owners", new PropertyEquals(EntityType.Item),
                    new HasProperty(PropertyType.Owner)),
            },
            Effects =
            {
                new Action("Create person",
                    new CreateEntity(EntityType.Person),
                    new SetProperty(new PropertyPath(0, PropertyType.Alive), true)
                ),
                new Action("Create item",
                    new CreateEntity(EntityType.Item),
                    new SetProperty(new PropertyPath(0, PropertyType.Owner), 0)),
                new Action("Someone dies",
                    new Assign(0, 
                    new And(new PropertyEquals(EntityType.Person), new PropertyEquals(PropertyType.Alive, true))),
                    new SetProperty(new PropertyPath(0, PropertyType.Alive), false)),
                // new Action("Set item owner",
                //     new And(new PropertyEquals( Properties.TypeItem), new PropertyEquals(PropertyType.Owner, default)),
                //     new SetProperty(new PropertyPath(PropertyType.Owner, new) PredicateParameter(
                //         new And(new PropertyEquals( Properties.TypePerson), new PropertyEquals(PropertyType.Alive, true))
                //     ))),
                new Action("Set item owner",
                    new Assign(0,
                        new And(new PropertyEquals(EntityType.Person), new PropertyEquals(PropertyType.Alive, true))
                    ),
                    new Assign(1,
                        new And(new PropertyEquals(EntityType.Item),
                            new And(new PropertyEquals(PropertyType.Owner, 0),
                                new PropertyNotEquals(PropertyType.Owner, new ComputedValue(new PropertyPath(1)))))
                    ),
                    new SetProperty(new PropertyPath(1, PropertyType.Owner),new ComputedValue(new PropertyPath(0)))),
                new Action("Two people marry",
                    new Assign(0, new And(
                        new PropertyEquals(EntityType.Person),
                        new PropertyEquals(PropertyType.Alive, true),
                        new PropertyEquals(PropertyType.Partner, 0))),
                    new Assign(1, new And(
                        new PropertyEquals(EntityType.Person),
                        new PropertyNotEquals(PropertyType.Id, new ComputedValue(0)),
                        new PropertyEquals(PropertyType.Alive, true),
                        new PropertyEquals(PropertyType.Partner, 0))) ,
                    new SetProperty(new PropertyPath(0, PropertyType.Partner), new ComputedValue(1)),
                    new SetProperty(new PropertyPath(1, PropertyType.Partner), new ComputedValue(0))
                ),
                new Action("Two people separate",
                    new Assign(0, new And(
                        new PropertyEquals(EntityType.Person),
                        new PropertyEquals(PropertyType.Alive, true),
                        new PropertyNotEquals(PropertyType.Partner, 0))),
                    new Assign(1, new And(
                        new PropertyEquals(EntityType.Person),
                        new PropertyNotEquals(PropertyType.Id, new ComputedValue(0)),
                        new PropertyEquals(PropertyType.Alive, true),
                        new PropertyEquals(PropertyType.Partner, new ComputedValue(0)))),
                    new SetProperty(new PropertyPath(0, PropertyType.Partner), 0),
                    new SetProperty(new PropertyPath(1, PropertyType.Partner), 0)
                ),
                // new Action("item_sold",
                //     new Assign(),
                //     new PredicateParameter(new And(new PropertyEquals(EntityType.Person), new PropertyNotEquals(PropertyType.Id, 0))),
                //     new SetProperty(new PropertyPath(0, PropertyType.Owner), new ComputedValue(0))
                //     )
                // new Action("Set item owner2",
                //     
                //     new And(new PropertyEquals( Properties.TypeItem), new PropertyEquals(PropertyType.Owner, default)),
                //     new SetProperty(PropertyType.Owner, new PredicateParameter(
                //         new And(
                //             new PropertyEquals( Properties.TypePerson),
                //             new PropertyEquals(PropertyType.Alive, true),
                //             new PropertyEquals(PropertyType.Owner,  ))
                //     ))),
            },
        };
        Console.WriteLine(StoryPrinter.Print(db.Effects));
    }
}

// TODO:
// parsing:
    // $i = pick(type=item, owner != null)
    // $p = pick(type=person, id != $i.owner) <- $i.owner doesn't work
// pick must be random
// add factions ?

// item.owner: x -> y gifted, stolen or inherited
// owner dies -> owned items have no owners

// generator : create fact


// rules:
// alive -> dies
// owner alive, item owned -> lost, given, stolen
// owner dead, item owned -> 