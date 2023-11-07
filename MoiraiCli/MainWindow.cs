using Terminal.Gui;

namespace Moirai;

public class MainWindow : Toplevel
{
    public Database Database;
    public StatusItem FileStatus, YearStatus;
    private View LeftPane;
    public MainWindow()
    {
        ColorScheme = Colors.TopLevel;
        LeftPane = new View()
        {
            X = 0,
            Y = 1,
            Height = Dim.Fill(1),
            Width = Dim.Percent(25),
        };
        ActionList = new ActionListView(this)
        {
            Height = Dim.Fill(),
            Width = Dim.Fill(),
            Shortcut = Key.CtrlMask | Key.A,
            CanFocus = true,
            ShortcutAction = () => ActionList.SetFocus(),
        };
        ActionList.Title = $"{ActionList.Title} ({ActionList.ShortcutTag})";
        RightPane = new EntityList(this)
        {
            X = Pos.Right(LeftPane),
            Y = 1,
            Height = Dim.Fill(1),
            Width = Dim.Fill(),
            CanFocus = true,
            Shortcut = Key.CtrlMask | Key.D,
            ShortcutAction = () => RightPane.SetFocus(),
        };
        EntityDetails = new EntityDetailsView(this)
        {
            Height = Dim.Fill(),
            Width = Dim.Fill(),
        };
        // RightPane.Title = $"{RightPane.Title} ({RightPane.ShortcutTag})";
        MenuBar = new MenuBar(new MenuBarItem[]
        {
            new MenuBarItem("_File", new MenuItem[]
            {
                new MenuItem("_Quit", "Quit UI Catalog", () => RequestStop(), null, null, Key.Q | Key.CtrlMask)
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
                new StatusItem(Key.CtrlMask | Key.ShiftMask | Key.A, "Actions", () =>
                {
                    ActionList.Visible = !ActionList.Visible;
                    RightPane.SetNeedsDisplay();
                    this.LayoutSubviews();
                }),
            }
        };
        LeftPane.Add(EntityDetails);
        // LeftPane.Add(ActionList);
        Add(MenuBar);
        Add(LeftPane);
        Add(RightPane);
        Add(StatusBar);
    }
    public ActionListView ActionList { get; set; }
    public EntityDetailsView EntityDetails { get; set; }
    public EntityList RightPane { get; set; }
    public void SelectEntity(int row)
    {
        EntityDetails.SetSelectedEntity(new EntityId(row + 1));
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
        RightPane.Update();
    }
    public void LoadDatabase(Database db)
    {
        Database = db;
        FileStatus.Title = "Loaded";
        ActionList.Load();
        RightPane.Load();
        UpdateDb();
    }
}