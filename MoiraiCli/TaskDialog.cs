using System.Diagnostics;
using Terminal.Gui;

namespace Moirai;

abstract class TaskDialog : Dialog
{
    public bool Canceled = true;
    protected Task _t;

    protected virtual void OnIdle(){}
    protected abstract Task CreateTask(CancellationToken cancellationToken, IProgress<int> progress);
    public TaskDialog(string title) : base(title)
    {
        _cts = new CancellationTokenSource();
        _progress = new Progress<int>(OnProgress);
        
        Func<bool> idle = default;

        this.Closing += e =>
        {
            _cts.Cancel();
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
        
        idle = Application.MainLoop.AddIdle(() =>
        {
            if (_t == null)
                return true;
            Ms = _sw.ElapsedMilliseconds;
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
        //         this.Add(new Label(10, 10, "Test"));
    }
    private Stopwatch _sw;
    public long Ms;
    protected CancellationTokenSource _cts;
    protected Progress<int> _progress;
    protected abstract void OnProgress(int obj);
}
