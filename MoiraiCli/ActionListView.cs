using System.Collections;
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
            _w.SelectAction((Action)e.Value);
        };
        _listView.OpenSelectedItem += e =>
        {
            if (_w.CurrentAction.Name == ((Action)e.Value).Name)
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
        _listView.Source = new ActionSource(_w);
    }

    class ActionSource : IListDataSource
    {
        private readonly MainWindow _w;

        public ActionSource(MainWindow w)
        {
            _w = w;
        }

        public void Render(ListView container, ConsoleDriver driver, bool selected, int item, int col, int line, int width,
            int start = 0)
        {
            var a = _w.Database.Actions[item];
            driver.AddStr(a.Name);
            driver.SetAttribute(container.GetHotNormalColor());
            foreach (var tagId in a.Tags)
            {
                driver.AddRune(' ');
                driver.AddStr(_w.Database.GetTagName(tagId));
            }
            driver.SetAttribute(container.GetNormalColor());

        }

        public bool IsMarked(int item)
        {
            return false;
        }

        public void SetMark(int item, bool value)
        {
           
        }

        public IList ToList() => _w.Database.Actions;

        public int Count => _w.Database.Actions.Count;
        public int Length => 100;
    }
}
