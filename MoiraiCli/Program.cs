using System.Collections.Concurrent;
using Pcg;

internal class Program
{
    public static async Task Main(string[] args)
    {
        string line;
        string path = "w.sg";
        ulong seed = 44;
        var db = MakeDb(path, seed);
        Console.WriteLine(db.Printer.Print());
        foreach (Action a in db.Actions)
        {
            if (a.IsStartAction)
                db.RunAction(a);
        }
        int prevAction = -1;
        int historyCount = 0;
        Pcg32 rnd = new(32, 57);

        bool watch = true;
        bool reload = false;
        if (watch)
        {
            FileSystemWatcher fsw = new FileSystemWatcher(Path.GetDirectoryName(Path.GetFullPath(path)));
            fsw.Filter = Path.GetFileName(path);
            fsw.NotifyFilter = NotifyFilters.LastWrite;
            fsw.Changed += (_, e) =>
            {
                Console.WriteLine("changed");
                reload = true;
            };
            fsw.EnableRaisingEvents = true;
        }

        Queue<string> replay = new(new string[]
        {
            // "create_time",
            // "char_born",
            // "char_born",
            // "pass_15_years",
            // "pass_15_years",
            // "wedding",
            // "make_item",
            // "make_item",
            // "make_item",
            // "make_item",
            // "make_item",
            // "couple_has_child",
            // "couple_has_child",
            // "couple_has_child",
            // "pass_15_years",
            // "pass_15_years",
            // "pass_15_years",
            // "pass_15_years",
            // "parent_dies",
            // "p",
            // "f",

        });

        ConcurrentQueue<string> lines = new();
        var t = Task.Run(() =>
        {
            while (true)
            {
                Console.Write("> ");
                lines.Enqueue(Console.ReadLine());
            }
        });

        bool printHistory = true;
        while (true)
        {
            bool fromQueue = replay.TryDequeue(out line);
            if (!fromQueue)
            {
                if (!lines.TryDequeue(out line) && !printHistory)
                {
                    await Task.Delay(200);
                    if (reload)
                    {
                        Console.WriteLine("RELOAD");
                        reload = false;
                        replay = new Queue<string>(db.History.Changesets.Select(c => c.ActionName));
                        db = MakeDb(path, seed);
                        printHistory = true;
                    }
                    continue;
                }
                // Console.WriteLine("-----------------");
                // for (var index = 0; index < db.Actions.Count; index++)
                // {
                //     var action = db.Actions[index];
                //     Console.WriteLine($"  {index:00} {action.Name}");
                // }
            }
            line ??= "";

            if (line == "qq")
                break;

            if (line == "t")
                db.PassYears(1);
            else if (line.StartsWith("t "))
            {
                if (int.TryParse(line.Substring("t ".Length), out var years))
                {
                    db.PassYears(years);
                }
            }
            else if (line.StartsWith("seed "))
            {
                if (ulong.TryParse(line.Substring("seed ".Length), out seed))
                {
                    replay = new Queue<string>(db.History.Changesets.Select(c => c.ActionName));
                    db = MakeDb(path, seed);
                }
            }
            else if (line == "p")
            {
                db.PrintDb();
            }
            else if (line == "h")
            {
                Console.WriteLine(string.Join("\n", db.History.Changesets.Select(c => c.ActionName)));
            }
            else if (line == "r")
            {
                prevAction = -1;
                RunRandomAction(db, rnd);
            }
            else if (line == "cs")
            {
                foreach (var cs in db.History.Changesets)
                {
                    db.Printer.PrintChangeset(cs);
                }
            }
            else if (line == "f" || (printHistory && !fromQueue))
            {
                printHistory = false;
                long year = -1;
                foreach (var cs in db.History.Changesets)
                {
                    if (cs.Year != year)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine(cs.Year);
                        Console.ResetColor();
                        year = cs.Year;
                    }
                    if (!string.IsNullOrEmpty(cs.Description))
                        Console.WriteLine(cs.Description);
                }
            }
            else if (line == "")
            {
                db.PassYears(1);
                // if (prevAction == -1)
                //     RunRandomAction(db, rnd);
                // else
                //     db.RunAction(db.Actions[prevAction]);
                // db.PrintDb();
            }
            else if (int.TryParse(line, out var i) && i >= 0 && i < db.Actions.Count)
            {
                prevAction = i;
                db.RunAction(db.Actions[i]);
                // db.PrintDb(); 
            }
            else
            {
                var indexOf = line.IndexOf(' ');
                int count = 1;
                if (indexOf != -1)
                {
                    count = int.Parse(line.Substring(indexOf + 1));
                    line = line.Substring(0, indexOf - 1);
                }
                foreach (var a in db.Actions)
                {
                    if (a.Name == line)
                    {
                        for (int j = 0; j < count; j++)
                        {
                            db.RunAction(a);
                        }
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
    private static Database MakeDb(string path, ulong seed)
    {

        var db = StoryParser.Parse(File.ReadAllText(path), out var errors);
        db.SetSeed(seed);
        db.History = new();
        return db;
    }
    private static void RunRandomAction(Database db, Pcg32 rnd)
    {

        Action a;
        do
        {
            a = db.Actions[(int)rnd.GenerateNext((uint)db.Actions.Count)];
            if (a.IsStartAction)
                continue;

            Console.WriteLine("try " + a.Name);
        } while (!db.RunAction(a));
    }
}