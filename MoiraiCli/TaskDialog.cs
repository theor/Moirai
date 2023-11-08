using System.Diagnostics;
using Terminal.Gui;

namespace Moirai;

abstract class TaskDialog : Dialog
{
    public bool Canceled = true;
    private Task _t;

    protected virtual void OnIdle(){}
    protected abstract Task CreateTask(CancellationToken cancellationToken, IProgress<int> progress);
    public TaskDialog(string title) : base(title)
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        Progress<int> progress = new Progress<int>(OnProgress);
        var idle = Application.MainLoop.AddIdle(() =>
        {
            OnIdle();
            // Debug.WriteLine("completed: " + _t.IsCompleted);
            if (_t.IsCompleted)
            {
                Ms = _sw.ElapsedMilliseconds;
                _sw.Stop();
                Canceled = !_t.IsCompletedSuccessfully;
                Application.RequestStop();
                return false;
            }
            return true;
        });

        this.Closing += e =>
        {
            cts.Cancel();
            Debug.WriteLine("Closing");
            Application.MainLoop.RemoveIdle(idle);
            _sw.Stop();
        };
        var button = new Button("Cancel", true);
        button.Clicked += () =>
        {
            _sw.Stop();
            Application.RequestStop();

        };
        AddButton(button);
        _sw = Stopwatch.StartNew();
        _t = CreateTask(cts.Token, progress);
        //         this.Add(new Label(10, 10, "Test"));
    }
    private Stopwatch _sw;
    public long Ms;
    protected abstract void OnProgress(int obj);
}