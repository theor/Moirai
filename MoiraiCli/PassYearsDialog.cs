using System.Diagnostics;
using Terminal.Gui;

namespace Moirai;

class PassYearsDialog : TaskDialog
{
    private readonly Database _db;
    private readonly long _years;
    private readonly bool _offset;
    public ProgressBar PulseProgressBar { get; set; }
    public Label Label { get; set; }
    public PassYearsDialog(Database db, long years, bool offset) : base($"Passing ${years} years")
    {
        _db = db;
        _years = years;
        _offset = offset;
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
             
        _t = CreateTask(_cts.Token, _progress);   
        Application.Run(this);
        
        return tcs.Task;
    }

    protected override Task CreateTask(CancellationToken cancellationToken, IProgress<int> progress)
    {
        return Task.Run(() =>
        {
            _db.Ctx.PassYears(_years, cancellationToken, progress, _offset);
            _db.Commit();
        }, cancellationToken);
    }
    protected override void OnProgress(int passed)
    {
        PulseProgressBar.Fraction = passed / (float)_years;
        Label.Text = $"{passed} / {_years} years - {Ms/1000}s";
    }
}
