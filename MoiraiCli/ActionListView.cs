using Terminal.Gui;

namespace Moirai;

public class ActionListView : FrameView
{
    private readonly MainWindow _w;
    private ListView _listView;
    public ActionListView(MainWindow w) : base("Actions")
    {
        _w = w;
        // ColorScheme = Colors.Base;
        _listView = new ListView
        {
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _listView.OpenSelectedItem += e =>
        {
            // Program.Db.run
        };
        // this.Add(new TextField("Years"){Width = Dim.Fill(0), Height = 1});
        this.Add(_listView);
        var _scrollBar = new ScrollBarView(_listView, true);

        _scrollBar.ChangedPosition += () =>
        {
            _listView.TopItem = _scrollBar.Position;
            if (_listView.TopItem != _scrollBar.Position)
            {
                _scrollBar.Position = _listView.TopItem;
            }
            _listView.SetNeedsDisplay();
        };
        _listView.DrawContent += (e) =>
        {
            if (_listView.Source == null) return;

            _scrollBar.Size = _listView.Source.Count - 1;
            _scrollBar.Position = _listView.TopItem;
            _scrollBar.Refresh();
        };
    }
    public void Load()
    {
        _listView.SetSource(_w.Database.Actions.Select(a => a.Name).ToList());
    }
}