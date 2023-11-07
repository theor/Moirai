using Moirai.Core;
using Terminal.Gui;

namespace Moirai;

public class MainWindow : Toplevel
{
    public Database Database;
    public StatusItem FileStatus, YearStatus;
    private View LeftPane;
    private List<EntityId> _history = new();
    private int _historyIndex;
    private EntityId Current => _historyIndex < _history.Count ? _history[_historyIndex] : default;
    public MainWindow()
    {
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
        LeftPane = new View()
        {
            X = 0,
            Y = 1,
            Height = Dim.Fill(1),
            Width = Dim.Percent(40),
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
        ActionList.Title = $"{ActionList.Title} ({ActionList.ShortcutTag})";
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
                new MenuItem("_Quit", "Quit UI Catalog", () => RequestStop(), null, null, Key.Q | Key.CtrlMask)
            }),
            new MenuBarItem("_View", new MenuItem[]
            {
                new MenuItem("Go to _Previous", "Select the previous entity", GoBack, null, null, Key.AltMask | Key.CursorLeft),
                new MenuItem("Go to _Next", "Select the next entity", GoForward, null, null, Key.AltMask | Key.CursorRight),
            }),
        });
        FileStatus = new StatusItem(Key.CharMask, "Driver:", null);
        YearStatus = MakeYearsStatus();
        StatusBar = new StatusBar()
        {
            Visible = true,
            Items = new StatusItem[]
            {
                FileStatus,
                YearStatus,
                new StatusItem(Key.CtrlMask | Key.ShiftMask | Key.D1, "Actions/Details", () =>
                {
                    ActionList.Visible = !ActionList.Visible;
                    EntityDetails.Visible = !ActionList.Visible;
                }),
                new StatusItem(Key.CtrlMask | Key.ShiftMask | Key.D2, "Entities/History", () =>
                {
                    EntityList.Visible = !EntityList.Visible;
                    WorldHistory.Visible = !EntityList.Visible;
                }),
            }
        };
        LeftPane.Add(EntityDetails);
        LeftPane.Add(ActionList);
        Add(MenuBar);
        Add(LeftPane);
        Add(EntityList);
        Add(WorldHistory);
        Add(StatusBar);
    }

    public ActionListView ActionList { get; set; }
    public EntityDetailsView EntityDetails { get; set; }
    public EntityList EntityList { get; set; }
    public WorldHistoryView WorldHistory { get; set; }

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
    private StatusItem MakeYearsStatus()
    {

        return new StatusItem(Key.CtrlMask | Key.T, "~^T~ Year: ", () =>
        {
            var dialog = new Wizard("Pass years");
            var wizardStep = new Wizard.WizardStep("Pass years")
            {
                HelpText = "Time passing will trigger multiple events according to their probability or filter",
            };
            int delta = 1;
            var label1 = new Label("Years ") { X = Pos.Center(), Y = Pos.Center() };
            var label2 = new Label("Result ") { X = Pos.Center(), Y = Pos.Center() + 2 };
            wizardStep.Add(label1);
            wizardStep.Add(label2);

            var textField = new TextField(delta.ToString())
            {
                Width = 20,
                X = Pos.Right(label1),
                Y = label1.Y,
                CanFocus = true,
                TabIndex = 0,
            };
            var result = new Label((Database.Ctx._year + delta).ToString())
            {
                Width = 20,
                X = Pos.Right(label2),
                Y = label2.Y,
            };
            textField.TextChanged += e =>
            {
                if (int.TryParse(textField.Text.ToString(), out var d))
                {
                    delta = d;
                    result.Text = (Database.Ctx._year + delta).ToString();
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
            dialog.Finished += e =>
            {
                if (delta > 0)
                {
                    Database.Ctx.PassYears(delta);
                    UpdateDb();
                }
            };
            // dialog.FocusFirst();
            Application.Run(dialog);
        });
    }
    private void UpdateDb()
    {
        YearStatus.Title = "~^T~ Year: " + Database.Ctx._year;
        EntityList.Update();
        WorldHistory.Update();
    }
    public void LoadDatabase(Database db)
    {
        Database = db;
        FileStatus.Title = "Loaded";
        ActionList.Load();
        EntityList.Load();
        UpdateDb();
    }
}