using Pcg;
using Pcg.Core;

internal class Program
{
    public static void Main(string[] args)
    {
        string line;
        string path = "w.sg";
        var db = StoryParser.Parse(File.ReadAllText(path), out var errors);
        db.SetSeed(44);
        db.History = new();
        Console.WriteLine(db.Printer.Print());
        int prevAction = -1;
        int historyCount = 0;
        Pcg32 rnd = new(32, 57);

        Queue<string> replay = new(new[]
        {
            "char_born",
            "char_born",
            "wedding",
            "make_item",
            "make_item",
            "make_item",
            "make_item",
            "make_item",
            "couple_has_child",
            "couple_has_child",
            "couple_has_child",
            // "parent_dies",
            // "p",
            // "f",
            
        });
           

        while (true)
        {
            bool fromQueue = replay.TryDequeue(out line);
            if (!fromQueue)
            {
                Console.Write("> ");
                line = Console.ReadLine() ?? "";
                Console.WriteLine("-----------------");
                for (var index = 0; index < db.Effects.Count; index++)
                {
                    var action = db.Effects[index];
                    Console.WriteLine($"  {index:00} {action.Name}");
                }
            }
            if (line == "qq")
                break;

            if (line == "p")
            {
                db.PrintDb();
            }
            else if (line == "r")
            {
                prevAction = -1;
                RunRandomAction(db, rnd);
            }
            else if (line == "h")
            {
                foreach (var cs in db.History.Changesets)
                {
                    db.Printer.PrintChangeset(cs);
                }
            }
            else if (line == "f")
            {
                foreach (var cs in db.History.Changesets)
                {
                    if(!string.IsNullOrEmpty(cs.Description))
                        Console.WriteLine(cs.Description);
                }
            }
            else if (line == "")
            {
                if (prevAction == -1)
                    RunRandomAction(db, rnd);
                else
                    db.RunAction(db.Effects[prevAction]);
                // db.PrintDb();
            }
            else if (int.TryParse(line, out var i) && i >= 0 && i < db.Effects.Count)
            {
                prevAction = i;
                db.RunAction(db.Effects[i]);
                // db.PrintDb();
            }
            else
            {
                foreach (var a in db.Effects)
                {
                    if (a.Name == line)
                    {
                        db.RunAction(a);
                        break;
                    }
                }
            }
            while (historyCount < db.History.Changesets.Count)
            {
                var cs = db.History.Changesets[historyCount++];
                db.Printer.PrintChangeset(cs, false);
            }

        }
    }
    private static void RunRandomAction(Database db, Pcg32 rnd)
    {

        Action a;
        do
        {
            a = db.Effects[(int)rnd.GenerateNext((uint)db.Effects.Count)];
            Console.WriteLine("try " + a.Name);
        } while (!db.RunAction(a));
    }
    // private static void Sample()
    // {
    //
    //     var db = new Database
    //     {
    //         Rules =
    //         {
    //             new Rule("Persons have liveliness", new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
    //                 new HasProperty(PropertyType.Alive)),
    //             new Rule("Items have owners", new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Item),
    //                 new HasProperty(PropertyType.Owner)),
    //         },
    //         Effects =
    //         {
    //             new Action("Create person",
    //                 new CreateEntity(0, EntityType.Person),
    //                 new SetProperty(new PropertyPath(0, PropertyType.Alive), true)
    //             ),
    //             new Action("Create item",
    //                 new CreateEntity(0, EntityType.Item),
    //                 new SetProperty(new PropertyPath(0, PropertyType.Owner), 0)),
    //             new Action("Someone dies",
    //                 new AssignPick(0,
    //                     new And(new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
    //                         new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true))),
    //                 new SetProperty(new PropertyPath(0, PropertyType.Alive), false)),
    //             // new Action("Set item owner",
    //             //     new And(new PropertyOperator(PropertyOperator.Operator.Equals, Properties.TypeItem), new PropertyOperator(PropertyOperator.Operator.Equals,PropertyType.Owner, default)),
    //             //     new SetProperty(new PropertyPath(PropertyType.Owner, new) PredicateParameter(
    //             //         new And(new PropertyOperator(PropertyOperator.Operator.Equals, Properties.TypePerson), new PropertyOperator(PropertyOperator.Operator.Equals,PropertyType.Alive, true))
    //             //     ))),
    //             new Action("Set item owner",
    //                 new AssignPick(0,
    //                     new And(new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
    //                         new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true))
    //                 ),
    //                 new AssignPick(1,
    //                     new And(new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Item),
    //                         new And(new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Owner, 0),
    //                             new PropertyOperator(PropertyOperator.Operator.NotEquals, PropertyType.Owner,
    //                                 new ComputedValue(new PropertyPath(1)))))
    //                 ),
    //                 new SetProperty(new PropertyPath(1, PropertyType.Owner), new ComputedValue(new PropertyPath(0)))),
    //             new Action("Two people marry",
    //                 new AssignPick(0, new And(
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true),
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Partner, 0))),
    //                 new AssignPick(1, new And(
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
    //                     new PropertyOperator(PropertyOperator.Operator.NotEquals, PropertyType.Id, new ComputedValue(0)),
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true),
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Partner, 0))),
    //                 new SetProperty(new PropertyPath(0, PropertyType.Partner), new ComputedValue(1)),
    //                 new SetProperty(new PropertyPath(1, PropertyType.Partner), new ComputedValue(0))
    //             ),
    //             new Action("Two people separate",
    //                 new AssignPick(0, new And(
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true),
    //                     new PropertyOperator(PropertyOperator.Operator.NotEquals, PropertyType.Partner, 0))),
    //                 new AssignPick(1, new And(
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, EntityType.Person),
    //                     new PropertyOperator(PropertyOperator.Operator.NotEquals, PropertyType.Id, new ComputedValue(0)),
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Alive, true),
    //                     new PropertyOperator(PropertyOperator.Operator.Equals, PropertyType.Partner, new ComputedValue(0)))),
    //                 new SetProperty(new PropertyPath(0, PropertyType.Partner), 0),
    //                 new SetProperty(new PropertyPath(1, PropertyType.Partner), 0)
    //             ),
    //             // new Action("item_sold",
    //             //     new Assign(),
    //             //     new PredicateParameter(new And(new PropertyOperator(PropertyOperator.Operator.Equals,EntityType.Person), new PropertyOperator(PropertyOperator.Operator.NotEquals,  PropertyType.Id, 0))),
    //             //     new SetProperty(new PropertyPath(0, PropertyType.Owner), new ComputedValue(0))
    //             //     )
    //             // new Action("Set item owner2",
    //             //     
    //             //     new And(new PropertyOperator(PropertyOperator.Operator.Equals, Properties.TypeItem), new PropertyOperator(PropertyOperator.Operator.Equals,PropertyType.Owner, default)),
    //             //     new SetProperty(PropertyType.Owner, new PredicateParameter(
    //             //         new And(
    //             //             new PropertyOperator(PropertyOperator.Operator.Equals, Properties.TypePerson),
    //             //             new PropertyOperator(PropertyOperator.Operator.Equals,PropertyType.Alive, true),
    //             //             new PropertyOperator(PropertyOperator.Operator.Equals,PropertyType.Owner,  ))
    //             //     ))),
    //         },
    //     };
    //     Console.WriteLine(StoryPrinter.Print(db.Effects));
    // }
}


// item.owner: x -> y gifted, stolen or inherited
// owner dies -> owned items have no owners

// generator : create fact


// rules:
// alive -> dies
// owner alive, item owned -> lost, given, stolen
// owner dead, item owned -> 