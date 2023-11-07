using System.Collections;
using System.Diagnostics;
using Moirai.Core;
using NStack;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace Moirai;

public class WorldHistoryView : View
{
    private readonly MainWindow _w;
    private readonly ListView _listView;

    public class MouseListView : ListView
    {
        private readonly MainWindow _w;
        public MouseListView(MainWindow w)
        {
            _w = w;
            Source = new HistorySource(w, this);
        }
        public override bool OnMouseEvent(MouseEvent mouseEvent)
        {
            if ((mouseEvent.Flags & MouseFlags.Button1Clicked) != 0)
            {
                var l = _ids[mouseEvent.Y];
                var r = l.FirstOrDefault(range => range.start <= mouseEvent.X && mouseEvent.X < range.start + range.length);
                if(!r.id.IsNull)
                    _w.SelectEntity(r.id);
                Debug.WriteLine(
                    $"{mouseEvent.X} {mouseEvent.Y} : {r.start}:{r.length} {r.id}");
            }
            return base.OnMouseEvent(mouseEvent);
        }
        List<List<(int start, int length, EntityId id)>> _ids = new();
        public void StartRow(int line)
        {
            if (line == 0)
            {
                _ids.Clear();
            }
            while (line >= _ids.Count)
                _ids.Add(new List<(int start, int length, EntityId id)>());
            _ids[line].Clear();
        }
        public void RegisterId(EntityId entityId, int x, int y, int length)
        {

            var l = _ids[y];
            l.Add((x, length, entityId));
        }
    }

    public WorldHistoryView(MainWindow w) : base("History")
    {
        _w = w;
        _listView = new MouseListView(_w)
        {
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Add(_listView);
    }

    public class HistorySource : IListDataSource
    {
        private readonly MainWindow _w;
        private readonly MouseListView _mlv;
        public HistorySource(MainWindow w, MouseListView mlv)
        {
            this._w = w;
            _mlv = mlv;
        }

        public void Render(ListView container, ConsoleDriver driver, bool selected, int item, int col, int line, int width, int start = 0)
        {
            container.Move(col, line);
            var cs = _w.Database.History.Changesets[item];
            var str = (cs.Description ?? cs.ActionName).ReplaceLineEndings(" - ");
            _mlv.StartRow(line);
            int index = 0;
            int displayedIndex = 0;
            while (index < str.Length)
            {
                var next = str.IndexOf("%id", index, StringComparison.Ordinal);
                if (next == -1)
                {
                    displayedIndex += str.Length - index;
                    driver.AddStr(str.Substring(index));
                    break;
                }
                driver.AddStr(str.Substring(index, next - index));
                displayedIndex += next - index;
                var end = str.IndexOf('%', next + 3);
                if (end == -1)
                    throw new System.InvalidOperationException(str);


                driver.SetAttribute(container.ColorScheme.HotFocus);
                var id = str.Substring(next + 3, end - next - 3);
                _mlv.RegisterId(new EntityId(long.Parse(id)), displayedIndex, line, id.Length);
                displayedIndex += id.Length;
                driver.AddStr(id);


                driver.SetAttribute(container.ColorScheme.Normal);
                index = end + 1;
            }
            // width -= TextFormatter.GetTextWidth(u);
            while (displayedIndex++ < width)
            // {
            driver.AddRune(' ');
            // }
        }
        public bool IsMarked(int item) => false;
        public void SetMark(int item, bool value)
        {
        }
        public IList ToList() => _w.Database?.History?.Changesets ?? new List<Changeset>();
        public int Count => _w.Database?.History?.Changesets?.Count ?? 0;
        public int Length => 0; //100;
    }

    public void Update()
    {
        _listView.SetNeedsDisplay();
        // _listView.Source = new HistorySource(_w);
    }
}