using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Channels;
using Moirai.Core;
using Terminal.Gui;

namespace Moirai;

public class MainWindow : Toplevel
{
    public Database Database;
    public StatusItem FileStatus, YearStatus, MessageStatus;
    private TabView LeftPane;
    private List<EntityId> _history = new();
    private int _historyIndex = -1;
    public EntityId Current => _historyIndex >= 0 && _historyIndex < _history.Count ? _history[_historyIndex] : default;
    public Action CurrentAction;
    public TagId CurrentTag;

    
    interface IMessage{}

    struct ReloadMessage : IMessage
    {
        public readonly string Path;
        public readonly int YearsToPass;
        public ReloadMessage(string path, int yearsToPass)
        {
            Path = path;
            YearsToPass = yearsToPass;
        }
    }

     static ChannelReader<IMessage> CreateWatcher(string path)
    {
        Channel<IMessage> channel = Channel.CreateBounded<IMessage>(new BoundedChannelOptions(40)
            { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true }); 
        new Thread(async () =>
        {
            FileSystemWatcher fsw = new FileSystemWatcher(Path.GetDirectoryName(Path.GetFullPath(path)));
            fsw.Filter = Path.GetFileName(path);
            fsw.NotifyFilter = NotifyFilters.LastWrite;

            while (true)
            {
                var res = fsw.WaitForChanged(WatcherChangeTypes.All, 1000);
                if(res.TimedOut)
                    continue;
                await channel.Writer.WaitToWriteAsync();
                Debug.WriteLine($"Changed: {res.ChangeType} {res.Name}");
                await channel.Writer.WriteAsync(new ReloadMessage(path, -1));
            }
        }).Start();
        return channel.Reader;
    }
    public MainWindow()
    {
        var args = Environment.GetCommandLineArgs();
        string path = args.Length > 1 ? args[1] : @"w.sg";

        ColorScheme = Colors.TopLevel;
        // AddCommand(Command.PageLeft, () =>
        // {
        //     GoBack();
        //     return true;
        // });
        // AddKeyBinding(Key.AltMask | Key.CursorLeft, Command.PageLeft);
        // AddCommand(Command.PageRight, () =>
        // {
        //     GoForward();
        //     return true;
        // });
        // AddKeyBinding(Key.AltMask | Key.CursorRight, Command.PageRight);
        LeftPane = new TabView()
        {
            
            X = 0,
            Y = 1,
            Height = Dim.Fill(1),
            Width =  Dim.Percent(20),
            Style = new TabView.TabStyle{TabsOnBottom = false}
        };
        TagList = new TagListView(this)
        {

            Height = Dim.Fill(),
            Width = Dim.Fill(),
            // Shortcut = Key.CtrlMask | Key.D1,
            CanFocus = true,
            // ShortcutAction = () => ActionList.SetFocus(),
            Visible = false,
        };
        CatList = new CatListView(this)
        {

            Height = Dim.Fill(),
            Width = Dim.Fill(),
            // Shortcut = Key.CtrlMask | Key.D1,
            CanFocus = true,
            // ShortcutAction = () => ActionList.SetFocus(),
            Visible = false,
        };
        ActionList = new ActionListView(this)
        {
            Height = Dim.Fill(),
            Width = Dim.Fill(),
            // Shortcut = Key.CtrlMask | Key.D1,
            CanFocus = true,
            // ShortcutAction = () => ActionList.SetFocus(),
            Visible = false,
        };
        EntityList = new EntityList(this)
        {
            X = Pos.Right(LeftPane),
            Y = 1,
            Height = Dim.Fill(1),
            Width = Dim.Fill(),
            CanFocus = true,
            Visible = false,

            // Shortcut = Key.CtrlMask | Key.D,
            // ShortcutAction = () => RightPane.SetFocus(),
        };
        EntityDetails = new EntityDetailsView(this)
        {
            Height = Dim.Fill(),
            Width = Dim.Fill(),
        };
        WorldHistory = new WorldHistoryView(this)
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            X = Pos.Right(LeftPane),
            Y=1,
        };
        // RightPane.Title = $"{RightPane.Title} ({RightPane.ShortcutTag})";
        MenuBar = new MenuBar(new MenuBarItem[]
        {
            new MenuBarItem("_File", new MenuItem[]
            {
                new MenuItem("_Reload", "Reload current file", async () => await ReloadFile(Database.FilePath), null, null, Key.F5),
                new MenuItem("_Quit", "Quit UI Catalog", () => RequestStop(), null, null, Key.Q | Key.CtrlMask),
            }),
            new MenuBarItem("_View", new MenuItem[]
            {
                new MenuItem("Go to _Previous", "Select the previous entity", GoBack, null, null, Key.AltMask | Key.CursorLeft),
                new MenuItem("Go to _Next", "Select the next entity", GoForward, null, null, Key.AltMask | Key.CursorRight),
            }),
            new MenuBarItem("_Generate", new MenuItem[]
            {
               new MenuItem("Family _Tree", "generate a graphviz family tree", GenerateFamilyTree, null, null, Key.F6), 
            }),
        });
        FileStatus = new StatusItem(Key.CharMask, "Driver:", null);
        YearStatus = MakeYearsStatus();
        MessageStatus = new StatusItem(Key.Unknown, "", null);
        StatusBar = new StatusBar()
        {
            Visible = true,
            Items = new StatusItem[]
            {
                FileStatus,
                YearStatus,
                new StatusItem( Key.F1, "Actions/Details", () =>
                {
                    if (LeftPane.SelectedTab.View == EntityDetails)
                        LeftPane.SelectedTab = LeftPane.Tabs.ElementAt(1);
                    else if (LeftPane.SelectedTab.View == ActionList)
                        LeftPane.SelectedTab = LeftPane.Tabs.ElementAt(2);
                    else if (LeftPane.SelectedTab.View == TagList)
                        LeftPane.SelectedTab = LeftPane.Tabs.ElementAt(3);
                    else if (LeftPane.SelectedTab.View == CatList)
                        LeftPane.SelectedTab = LeftPane.Tabs.ElementAt(0);
                    // ActionList.Visible = !ActionList.Visible;
                    // EntityDetails.Visible = !ActionList.Visible;
                }),
                new StatusItem(Key.F2, "Entities/History", () =>
                {
                    EntityList.Visible = !EntityList.Visible;
                    WorldHistory.Visible = !EntityList.Visible;
                }),
                MessageStatus,
            }
        };
        LeftPane.AddTab(new TabView.Tab( "Entities", EntityDetails), false);
        LeftPane.AddTab(new TabView.Tab( "Actions", ActionList), false);
        LeftPane.AddTab(new TabView.Tab( "Tags", TagList), false);
        LeftPane.AddTab(new TabView.Tab( "Categories", CatList), false);
        // LeftPane.Add(EntityDetails);
        // LeftPane.Add(ActionList);
        Add(MenuBar);
        Add(LeftPane);
        Add(EntityList);
        Add(WorldHistory);
        Add(StatusBar);


        var reader = CreateWatcher(path);
        ReloadFile(path);
        Application.MainLoop.AddIdle( () =>
        {
            if (reader.TryRead(out var msg))
            {
                ReloadFile(((ReloadMessage)msg).Path);
            }
            return true;
        });
        Application.RootMouseEvent += e =>
        {
            if((e.Flags & MouseFlags.ReportMousePosition) == 0)
                Debug.WriteLine(e.Flags);
            if ((e.Flags == (MouseFlags.ButtonAlt| MouseFlags.Button3Released)))
            {
                    e.Handled = true;
                    GoBack();
            }
            
            if ((e.Flags == (MouseFlags.ButtonAlt| MouseFlags.ButtonShift| MouseFlags.Button3Released)))
            {
                e.Handled = true;
                GoForward();
            }
        };

    }

    private void GenerateFamilyTree()
    {
        List<EntityId> pool = new();
        Database.FindAll(new IsOfType(new PropertyPath(0), Database.GetEntityType("Link").Id), ref pool);
        var parentProp = Database.GetPropertyId("parent");
        var childProp = Database.GetPropertyId("child");
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("digraph G {\n  graph [splines=ortho];\n  node [shape=box];");
        // Dictionary<EntityId, (EntityId, EntityId)> ids = new();
        HashSet<EntityId> ids = new();
        foreach (var linkId in pool)
        {
            Database.GetProperty(linkId, parentProp, out var parent);
            Database.GetProperty(linkId, childProp, out var child);
            sb.AppendLine($"{parent.IntValue} -> {child.IntValue}");
            ids.Add(parent.Id);
            ids.Add(child.Id);

        }
        // foreach (var linkId in pool)
        // {
        //     Database.GetProperty(linkId, parentProp, out var parent);
        //     Database.GetProperty(linkId, childProp, out var child);
        //     if(!ids.TryGetValue(child.Id, out var existing))
        //         ids.Add(child.Id, (parent.Id, EntityId.Null));
        //     else
        //     {
        //         ids[child.Id] = (existing.Item1, parent.Id);
        //         var (a, b) = (existing.Item1.Id, parent.IntValue);
        //         sb.AppendLine($"n{Math.Min(a,b)}_{Math.Max(a,b)} -> {child.IntValue}");
        //     }
        // }
        //
        // foreach (var (a,b) in ids.Values.Distinct())
        // {
        //     var n = $"n{Math.Min(a.Id, b.Id)}_{Math.Max(a.Id, b.Id)}";
        //     sb.AppendLine($"{n}[label=\"\" shape=circle]");
        //     // sb.AppendLine($"{{ rank = same;");
        //     sb.AppendLine($"{a.Id} -> {n}");
        //     sb.AppendLine($"{b.Id} -> {n}");
        //     // sb.AppendLine("}");
        // }
        foreach (var eid in ids)
        {
            Database.GetProperty(eid, Database.PropName, out var name);
            sb.AppendLine($"{eid.Id}[label=\"{name.Value}\"]");
        }
       
        sb.AppendLine("}");
        
        File.WriteAllText(@"C:\Users\theor\Moirai\MoiraiCli\g.dot", sb.ToString());

       // Process.Start(new  ProcessStartInfo("https://edotor.net/?engine=dot#" + Uri.EscapeDataString(sb.ToString())){UseShellExecute = true});
    }

    private Dialog? errorDialog;
    public async Task ReloadFile(string path)
    {
        if (errorDialog != null)
        {
            errorDialog.RequestStop();
            errorDialog = null;
        }
        Debug.WriteLine("Reloading " + path);
        var targetYear = Database?.Ctx?.Year ?? 0;
        string content = File.ReadAllText(path);
        var db = StoryParser.Parse(content, out var errors);

        if (errors.Count != 0)
        {
            errorDialog = new Dialog("Errors"){Modal = false};
            errorDialog.Add(new Label(string.Join("\n", errors))
            {
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            });
            Application.Run(errorDialog);
            return;
        }
        
        db.FilePath = path;
        db.History = new();

        db.Init();
        await new PassYearsDialog(db, targetYear, false).Execute();
        // db.Ctx.PassYears(100);
        LoadDatabase(db);
    }

    public ActionListView ActionList { get; set; }
    public TagListView TagList { get; set; }
    public CatListView CatList { get; set; }
    public EntityDetailsView EntityDetails { get; set; }
    public EntityList EntityList { get; set; }
    public WorldHistoryView WorldHistory { get; set; }

    public void SelectAction(Action a)
    {
        CurrentAction = a;
        if(_mode == FilteringMode.Action)
            WorldHistory.SetFiltering(_mode);
    }
    public void SelectEntity(EntityId entityId, bool addToHistory = true)
    {
        if (addToHistory)
        {
            if(_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex+1, _history.Count - _historyIndex-1);
            _history.Add(entityId);
            _historyIndex++;
        }
        EntityDetails.SetSelectedEntity(entityId);
        if(_mode == FilteringMode.Entity)
            WorldHistory.SetFiltering(_mode);
    }
    public void GoBack()
    {
        if (_historyIndex > 0)
        {
            _historyIndex--;
                SelectEntity(Current, false);
        }
    }
    public void GoForward()
    {
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            SelectEntity(Current, false);
        }
    }
    private int _lastPassedYearsValue = 100;
    private StatusItem MakeYearsStatus()
    {

        return new StatusItem(Key.CtrlMask | Key.T, "~^T~ Year: ", () =>
        {
            var dialog = new Wizard("Pass years");
            var wizardStep = new Wizard.WizardStep("Pass years")
            {
                HelpText = "Time passing will trigger multiple events according to their probability or filter",
            };
            var label1 = new Label("Years ") { X = Pos.Center(), Y = Pos.Center() };
            var label2 = new Label("Result ") { X = Pos.Center(), Y = Pos.Center() + 2 };
            wizardStep.Add(label1);
            wizardStep.Add(label2);

            var textField = new TextField(_lastPassedYearsValue.ToString())
            {
                Width = 20,
                X = Pos.Right(label1),
                Y = label1.Y,
                CanFocus = true,
                TabIndex = 0,
            };
            var result = new Label((Database.Ctx.Year + _lastPassedYearsValue).ToString())
            {
                Width = 20,
                X = Pos.Right(label2),
                Y = label2.Y,
            };
            textField.TextChanged += e =>
            {
                if (int.TryParse(textField.Text.ToString(), out var d))
                {
                    _lastPassedYearsValue = d;
                    result.Text = (Database.Ctx.Year + _lastPassedYearsValue).ToString();
                    textField.ColorScheme = Colors.Base;
                }
                else
                {
                    textField.ColorScheme = Colors.Error;
                }
            };

            wizardStep.Add(textField);
            wizardStep.Add(result);

            dialog.AddStep(wizardStep);
            dialog.StepChanged += (_) => textField.SetFocus();
            dialog.Finished += async e =>
            {
                if (_lastPassedYearsValue <= 0)
                    return;
                var cw = new PassYearsDialog(Database, _lastPassedYearsValue, true);
                try
                {
                    await cw.Execute();
                }
                catch (TaskCanceledException)
                {
                }
                MessageStatus.Title = cw.Ms + "ms";
                UpdateDb();
            };
            // dialog.FocusFirst();
            Application.Run(dialog);
        });
    }

    private void UpdateDb()
    {
        YearStatus.Title = "~^T~ Year: " + Database.Ctx.Year;
        EntityList.Update();
        WorldHistory.Update();
    }
    public void LoadDatabase(Database db)
    {
        Database = db;
        FileStatus.Title = "Loaded";
        ActionList.Load();
        EntityList.Load();
        TagList.Load();
        CatList.Load();
        UpdateDb();
    }

    public enum FilteringMode
    {
        None,
        Entity,
        Action,
        Tag,
        Category
    }

    private FilteringMode _mode;

    public void SetFiltering(FilteringMode filtering)
    {
        _mode = filtering;
        WorldHistory.SetFiltering(filtering);
    }
    public void SetDisplayedProperty(PropertyId id, bool displayed)
    {
        EntityList.SetPropertyColumn(id, displayed);
    }

    public void SelectTag(TagId tagId)
    {
        CurrentTag = tagId;
    }

    public CategoryId CurrentCategory;
    public void SelectCategory(CategoryId categoryId)
    {
        CurrentCategory = categoryId;
    }
}
