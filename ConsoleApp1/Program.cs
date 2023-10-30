using Pcg.Core;

internal class Program
{
    public static void Main(string[] args)
    {
        string line;
        string path = "w.sg";
        var db = new Database() { Effects = StoryParser.Parse(File.ReadAllText(path), out var errors) };
        db.History = new();
        Console.WriteLine(StoryPrinter.Print(db.Effects));
        int prevAction = -1;
        int historyCount = 0;
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

            if (line == "p")
            {
                db.PrintDb();
            }
            if (line == "h")
            {
                foreach (var cs in db.History.Changesets)
                {
                    PrintChangeset(cs);
                }
            }
            else if (line == "" && prevAction >= 0)
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
            while (historyCount < db.History.Changesets.Count)
            {
                var cs = db.History.Changesets[historyCount++];
                PrintChangeset(cs);
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
    private static void PrintChangeset(Changeset cs)
    {

        Console.WriteLine(cs.ActionName);
        foreach (var change in cs.Changes)
        {
            Console.WriteLine("  " + change);
        }
    }
    private static void Sample()
    {

        var db = new Database
        {
            Rules =
            {
                new Rule("Persons have liveliness", new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
                    new HasProperty(PropertyType.Alive)),
                new Rule("Items have owners", new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Item),
                    new HasProperty(PropertyType.Owner)),
            },
            Effects =
            {
                new Action("Create person",
                    new CreateEntity(0, EntityType.Person),
                    new SetProperty(new PropertyPath(0, PropertyType.Alive), true)
                ),
                new Action("Create item",
                    new CreateEntity(0, EntityType.Item),
                    new SetProperty(new PropertyPath(0, PropertyType.Owner), 0)),
                new Action("Someone dies",
                    new AssignPick(0,
                        new And(new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
                            new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true))),
                    new SetProperty(new PropertyPath(0, PropertyType.Alive), false)),
                // new Action("Set item owner",
                //     new And(new PropertyOperator(PropertyOperator.Operator.Equals, Properties.TypeItem), new PropertyOperator(PropertyOperator.Operator.Equals,PropertyType.Owner, default)),
                //     new SetProperty(new PropertyPath(PropertyType.Owner, new) PredicateParameter(
                //         new And(new PropertyOperator(PropertyOperator.Operator.Equals, Properties.TypePerson), new PropertyOperator(PropertyOperator.Operator.Equals,PropertyType.Alive, true))
                //     ))),
                new Action("Set item owner",
                    new AssignPick(0,
                        new And(new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
                            new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true))
                    ),
                    new AssignPick(1,
                        new And(new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Item),
                            new And(new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Owner, 0),
                                new PropertyOperator(PropertyOperator.Operator.NotEquals, PropertyType.Owner,
                                    new ComputedValue(new PropertyPath(1)))))
                    ),
                    new SetProperty(new PropertyPath(1, PropertyType.Owner), new ComputedValue(new PropertyPath(0)))),
                new Action("Two people marry",
                    new AssignPick(0, new And(
                        new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
                        new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true),
                        new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Partner, 0))),
                    new AssignPick(1, new And(
                        new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
                        new PropertyOperator(PropertyOperator.Operator.NotEquals, PropertyType.Id, new ComputedValue(0)),
                        new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true),
                        new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Partner, 0))),
                    new SetProperty(new PropertyPath(0, PropertyType.Partner), new ComputedValue(1)),
                    new SetProperty(new PropertyPath(1, PropertyType.Partner), new ComputedValue(0))
                ),
                new Action("Two people separate",
                    new AssignPick(0, new And(
                        new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
                        new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true),
                        new PropertyOperator(PropertyOperator.Operator.NotEquals, PropertyType.Partner, 0))),
                    new AssignPick(1, new And(
                        new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
                        new PropertyOperator(PropertyOperator.Operator.NotEquals, PropertyType.Id, new ComputedValue(0)),
                        new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true),
                        new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Partner, new ComputedValue(0)))),
                    new SetProperty(new PropertyPath(0, PropertyType.Partner), 0),
                    new SetProperty(new PropertyPath(1, PropertyType.Partner), 0)
                ),
                // new Action("item_sold",
                //     new Assign(),
                //     new PredicateParameter(new And(new PropertyOperator(PropertyOperator.Operator.Equals,EntityType.Person), new PropertyOperator(PropertyOperator.Operator.NotEquals,  PropertyType.Id, 0))),
                //     new SetProperty(new PropertyPath(0, PropertyType.Owner), new ComputedValue(0))
                //     )
                // new Action("Set item owner2",
                //     
                //     new And(new PropertyOperator(PropertyOperator.Operator.Equals, Properties.TypeItem), new PropertyOperator(PropertyOperator.Operator.Equals,PropertyType.Owner, default)),
                //     new SetProperty(PropertyType.Owner, new PredicateParameter(
                //         new And(
                //             new PropertyOperator(PropertyOperator.Operator.Equals, Properties.TypePerson),
                //             new PropertyOperator(PropertyOperator.Operator.Equals,PropertyType.Alive, true),
                //             new PropertyOperator(PropertyOperator.Operator.Equals,PropertyType.Owner,  ))
                //     ))),
            },
        };
        Console.WriteLine(StoryPrinter.Print(db.Effects));
    }
}

// TODO:
// clean variant value
// format history
// DONE keep diff during action run
// DONE parsing:
// DONE $i = pick(type=item, owner != null)
// DONE $p = pick(type=person, id != $i.owner) <- $i.owner doesn't work
// DONE pick must be random
// wip add factions ?

// item.owner: x -> y gifted, stolen or inherited
// owner dies -> owned items have no owners

// generator : create fact


// rules:
// alive -> dies
// owner alive, item owned -> lost, given, stolen
// owner dead, item owned -> 