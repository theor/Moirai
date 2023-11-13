using System.Collections.Concurrent;
using System.Diagnostics;
using Moirai;
using Moirai.Core;
using Terminal.Gui;

internal class Program
{
    public static async Task Main(string[] args)
    {

        Application.Run<MainWindow>();
        Application.Shutdown();
    }
    static async void F()
    {

        string line;
        string path = "w.sg";
        ulong seed = 44;
        var db = MakeDb(path, seed);
        Console.WriteLine(db.Printer.Print());
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
            fsw.Renamed += (_, e) =>
            {
                Console.WriteLine("changed");
                reload = true;
            };
            fsw.EnableRaisingEvents = true;
        }

        Queue<string> replay = new(new string[]
        {
            // "char_born",
            // "t 20",
            // "char_born",
            // "t 20",
            // "wedding",
            // "paint_item",
            // "paint_item",
            // "couple_has_child",
            // "roundtrip",
            // "t 40",


            // "deserialize",
            // "t 1",

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

            if (line == "deserialize")
                db.Deserialize(File.ReadAllText("db.json"));

            if (line == "serialize")
                File.WriteAllText("db.json", db.Serialize());
            if (line == "roundtrip")
            {
                var json1 = db.Serialize();
                File.WriteAllText("db.json", json1);
                db.Deserialize(json1);
                var json2 = db.Serialize();
                File.WriteAllText("db2.json", json2);
                if (json1 != json2)
                    throw new System.NotImplementedException("Diff");
            }
            if (line == "t")
                db.Ctx.PassYears(1, true);
            else if (line.StartsWith("t "))
            {
                if (int.TryParse(line.Substring("t ".Length), out var years))
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    db.Ctx.PassYears(years, true);
                    Console.WriteLine($"Time: {(sw.ElapsedMilliseconds / 1000f)}s");
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
                var ids = line.Substring(1).Split(" ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                db.Printer.PrintDb();
            }
            else if (line.StartsWith("p"))
            {
                foreach (var id in line.Substring(1).Split(" ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                             .Select(long.Parse))
                {
                    if (db.TryGetEntity(new EntityId(id), out var e))
                        db.Printer.PrintEntity(e);
                    else
                        Console.WriteLine($"#{id} not found");
                }
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
                foreach (var cs in db.Records)
                {
                    if (cs.Year != year)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine(cs.Year);
                        Console.ResetColor();
                        year = cs.Year;
                    }
                    if (!string.IsNullOrEmpty(cs.Text))
                        Console.WriteLine( /*cs.Year + ": " +*/ cs.Text);
                }
            }
            else if (line == "")
            {
                db.Ctx.PassYears(1, true);
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
            int maxPrinted = 100;
            while (historyCount < db.History.Changesets.Count)
            {
                var cs = db.History.Changesets[historyCount++];
                if (maxPrinted-- > 0)
                    db.Printer.PrintChangeset(cs, false);
            }

        }
    }
    private static Database MakeDb(string path, ulong seed)
    {

        var db = StoryParser.Parse(File.ReadAllText(path), out var errors);
        db.SetSeed(seed);
        db.History = new();

        db.Init();
        return db;
    }
    private static void RunRandomAction(Database db, Pcg32 rnd)
    {

        Action a;
        do
        {
            a = db.Actions[(int)rnd.GenerateNext((uint)db.Actions.Count)];
            if (a.Filter is FilterAtStart)
                continue;

            Console.WriteLine("try " + a.Name);
        } while (!db.RunAction(a));
    }
}
