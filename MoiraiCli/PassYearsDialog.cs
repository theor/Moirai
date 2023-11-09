using System.Diagnostics;
using Terminal.Gui;

namespace Moirai;

class PassYearsDialog : TaskDialog
{
    private readonly Database _db;
    private readonly int _years;
    public ProgressBar PulseProgressBar { get; set; }
    public Label Label { get; set; }
    public PassYearsDialog(Database db, int years) : base($"Passing ${years} years")
    {
        _db = db;
        _years = years;
        PulseProgressBar = new ProgressBar () {
            X = 1,
            Y = Pos.Center(),
            Width = Dim.Fill (),
            Height = 1,
            ColorScheme = Colors.Error,
                
        };
        Label = new Label
        {
            X= Pos.Center(),
            Y = Pos.Bottom(PulseProgressBar),
        };

        Add (PulseProgressBar);
        Add (Label);
    }
    public Task Execute()
    {
        TaskCompletionSource tcs = new TaskCompletionSource();
        this.Closed += e =>
        {
            Debug.WriteLine("Canceled: " + Canceled);
            if(Canceled)
                tcs.SetCanceled();
            else
                tcs.SetResult();
        };
                
        Application.Run(this);
        return tcs.Task;
    }

    protected override Task CreateTask(CancellationToken cancellationToken, IProgress<int> progress)
    {
        return Task.Run(() =>
        {
            _db.Ctx.PassYears(_years, cancellationToken, progress);
            _db.Commit();
        });
    }
    protected override void OnProgress(int passed)
    {
        PulseProgressBar.Fraction = passed / (float)_years;
        Label.Text = $"{passed} / {_years} years";
    }
}