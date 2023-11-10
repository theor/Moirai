using Terminal.Gui;

namespace Moirai;

public class ActionListView : FrameView
{
    private readonly MainWindow _w;
    private ListView _listView;
    private CheckBox _filterCheckBox;
    public ActionListView(MainWindow w) : base("Actions")
    {
        _w = w;
        // ColorScheme = Colors.Base;
        _listView = new ListView
        {
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMultipleSelection = true,
        };
        _listView.SelectedItemChanged += e =>
        {
            _w.SelectAction((string)e.Value);
        };
        _listView.OpenSelectedItem += e =>
        {
            if (_w.CurrentAction == (string)e.Value)
            {
                // _w.SelectAction(null);
                _w.SetFiltering(MainWindow.FilteringMode.None);
                _filterCheckBox.Checked =false;
            }
            else
            {
                _filterCheckBox.Checked =true;
                // _w.SelectAction((string)e.Value);
                _w.SetFiltering(MainWindow.FilteringMode.Action);
            }
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
        _filterCheckBox = new CheckBox("Filter"){ Y = Pos.AnchorEnd(1), X = Pos.AnchorEnd(10)};
        _filterCheckBox.Toggled += _ => _w.SetFiltering(_filterCheckBox.Checked ? MainWindow.FilteringMode.Action : MainWindow.FilteringMode.None);
        Add(_filterCheckBox);
    }
    public void Load()
    {
        _listView.SetSource(_w.Database.Actions.Select(a => a.Name).ToList());
    }
}