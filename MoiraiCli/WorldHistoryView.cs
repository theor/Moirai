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
    private readonly MouseListView _listView;

    public class MouseListView : ListView
    {
        private readonly MainWindow _w;
        public HistorySource HistorySource => (HistorySource)Source;
        public MouseListView(MainWindow w)
        {
            _w = w;
            Source = new HistorySource(w, this);
        }
        public override bool OnMouseEvent(MouseEvent mouseEvent)
        {
            if ((mouseEvent.Flags & MouseFlags.Button1Clicked) != 0)
            {
                if (mouseEvent.Y < _ids.Count)
                {
                    var l = _ids[mouseEvent.Y];
                    var r = l.FirstOrDefault(range => range.start <= mouseEvent.X && mouseEvent.X < range.start + range.length);
                    if (!r.id.IsNull)
                        _w.SelectEntity(r.id);
                    // Debug.WriteLine(
                    //     $"{mouseEvent.X} {mouseEvent.Y} : {r.start}:{r.length} {r.id}");
                }
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
            var cs = _filtering != MainWindow.FilteringMode.None ? _filtered[item] : _w.Database.Records[item];
            var str = (cs.Text).ReplaceLineEndings(" - ");
            _mlv.StartRow(line);
            // driver.AddStr(str);
            // return;
            int index = 0;
            int displayedIndex = 5;
            var yearString = cs.Year.ToString();
            for (int i = 0; i < displayedIndex; i++)
            {
                if(i < yearString.Length)
                    driver.AddRune(yearString[i]);
                else
                    driver.AddRune(' ');
            }
            while (index < str.Length)
            {
                var next = str.IndexOf("<#", index, StringComparison.Ordinal);
                
                // no other tag, add remaining line
                if (next == -1)
                {
                    displayedIndex += str.Length - index;
                    driver.AddStr(str.Substring(index));
                    break;
                }
                
                // found a tag opening
                driver.AddStr(str.Substring(index, next - index));
                displayedIndex += next - index;
                var startClose = str.IndexOf('>', next + 2);
                if (startClose == -1)
                    throw new System.InvalidOperationException(str);
    
                var id = str.Substring(next + 2, startClose - next - 2);
    
                // closing
                var endOpen = str.IndexOf("</>", startClose, StringComparison.Ordinal);
                if (endOpen == -1)
                    throw new System.InvalidOperationException(str);
    
                var content = str.Substring(startClose + 1, endOpen - startClose - 1);
    
                // add link tag
                var entityId = new EntityId(long.Parse(id));
                if(_w.Current.Id == entityId.Id)
                    driver.SetAttribute(container.ColorScheme.Disabled);
                else
                    driver.SetAttribute(/*selected ? container.ColorScheme.HotFocus : */container.ColorScheme.HotNormal);
                _mlv.RegisterId(entityId, displayedIndex, line, content.Length);
                displayedIndex += content.Length;
                driver.AddStr(content);
                driver.SetAttribute(/*selected ? container.ColorScheme.Focus : */container.ColorScheme.Normal);
                index = endOpen + 3;
            }
            while (displayedIndex++ < width)
                driver.AddRune(' ');
        }
        public bool IsMarked(int item) => false;
        public void SetMark(int item, bool value)
        {
        }
        public IList ToList() => _filtering != MainWindow.FilteringMode.None ? _filtered : _w.Database?.Records ?? new List<Database.Record>();
        public int Count => _filtering != MainWindow.FilteringMode.None ? _filtered.Count : _w.Database?.Records?.Count ?? 0;
        public int Length => 0; //100;
        
        private MainWindow.FilteringMode _filtering;
        private List<Database.Record> _filtered = new();
        private HashSet<EntityId> _changed = new();
        public void SetFiltering(MainWindow.FilteringMode filtering)
        {
            _filtering = filtering;
            switch (filtering)
            {
                case MainWindow.FilteringMode.Entity:
                    _filtered = _w.Database.Records.Where(r =>
                    {
                        if (r.ChangesetId == -1)
                            return false;
                        _changed.Clear();
                        var cs = _w.Database.History.Changesets[r.ChangesetId];
                        cs.GetAffectedEntities(_changed);
                        return _changed.Contains(_w.Current);
                    }).ToList();
                    break;
                case MainWindow.FilteringMode.Action:
                    _filtered = _w.Database.Records.Where(cs => cs.ActionId == _w.CurrentAction.Id).ToList();
                    break;
                case MainWindow.FilteringMode.Tag:
                    if (_w.CurrentTag.IsNull)
                        _filtered = _w.Database.Records;
                    else
                        _filtered = _w.Database.Records.Where(cs =>
                            (cs.Tags & (1ul << (int)(_w.CurrentTag.Id - 1))) != 0).ToList();
                    break;
                    
            }
        }
    }

    // public class HistorySource : IListDataSource
    // {
    //     private readonly MainWindow _w;
    //     private readonly MouseListView _mlv;
    //     public HistorySource(MainWindow w, MouseListView mlv)
    //     {
    //         this._w = w;
    //         _mlv = mlv;
    //     }
    //
    //     public void Render(ListView container, ConsoleDriver driver, bool selected, int item, int col, int line, int width, int start = 0)
    //     {
    //         container.Move(col, line);
    //         var cs = _filtering != MainWindow.FilteringMode.None ? _filtered[item] : _w.Database.History.Changesets[item];
    //         var str = (cs.ActionName).ReplaceLineEndings(" - ");
    //         _mlv.StartRow(line);
    //         // driver.AddStr(str);
    //         // return;
    //         int index = 0;
    //         int displayedIndex = 5;
    //         var yearString = cs.Year.ToString();
    //         for (int i = 0; i < displayedIndex; i++)
    //         {
    //             if(i < yearString.Length)
    //                 driver.AddRune(yearString[i]);
    //             else
    //                 driver.AddRune(' ');
    //         }
    //         while (index < str.Length)
    //         {
    //             var next = str.IndexOf("<#", index, StringComparison.Ordinal);
    //             
    //             // no other tag, add remaining line
    //             if (next == -1)
    //             {
    //                 displayedIndex += str.Length - index;
    //                 driver.AddStr(str.Substring(index));
    //                 break;
    //             }
    //             
    //             // found a tag opening
    //             driver.AddStr(str.Substring(index, next - index));
    //             displayedIndex += next - index;
    //             var startClose = str.IndexOf('>', next + 2);
    //             if (startClose == -1)
    //                 throw new System.InvalidOperationException(str);
    //
    //             var id = str.Substring(next + 2, startClose - next - 2);
    //
    //             // closing
    //             var endOpen = str.IndexOf("</>", startClose, StringComparison.Ordinal);
    //             if (endOpen == -1)
    //                 throw new System.InvalidOperationException(str);
    //
    //             var content = str.Substring(startClose + 1, endOpen - startClose - 1);
    //
    //             // add link tag
    //             var entityId = new EntityId(long.Parse(id));
    //             if(_w.Current.Id == entityId.Id)
    //                 driver.SetAttribute(container.ColorScheme.Disabled);
    //             else
    //                 driver.SetAttribute(/*selected ? container.ColorScheme.HotFocus : */container.ColorScheme.HotNormal);
    //             _mlv.RegisterId(entityId, displayedIndex, line, content.Length);
    //             displayedIndex += content.Length;
    //             driver.AddStr(content);
    //             driver.SetAttribute(/*selected ? container.ColorScheme.Focus : */container.ColorScheme.Normal);
    //             index = endOpen + 3;
    //         }
    //         while (displayedIndex++ < width)
    //             driver.AddRune(' ');
    //     }
    //     public bool IsMarked(int item) => false;
    //     public void SetMark(int item, bool value)
    //     {
    //     }
    //     public IList ToList() => _filtering != MainWindow.FilteringMode.None ? _filtered : _w.Database?.History?.Changesets ?? new List<Changeset>();
    //     public int Count => _filtering != MainWindow.FilteringMode.None ? _filtered.Count : _w.Database?.History?.Changesets?.Count ?? 0;
    //     public int Length => 0; //100;
    //     
    //     private MainWindow.FilteringMode _filtering;
    //     private List<Changeset> _filtered = new();
    //     private HashSet<EntityId> _changed = new();
    //     public void SetFiltering(MainWindow.FilteringMode filtering)
    //     {
    //         _filtering = filtering;
    //         switch (filtering)
    //         {
    //             case MainWindow.FilteringMode.Entity:
    //                 _filtered = _w.Database.History.Changesets.Where(cs =>
    //                 {
    //                     _changed.Clear();
    //                     cs.GetAffectedEntities(_changed);
    //                     return _changed.Contains(_w.Current);
    //                 }).ToList();
    //                 break;
    //             case MainWindow.FilteringMode.Action:
    //                 _filtered = _w.Database.History.Changesets.Where(cs => cs.ActionName == _w.CurrentAction.Name).ToList();
    //                 break;
    //             case MainWindow.FilteringMode.Tag:
    //                 if (_w.CurrentTag.IsNull)
    //                     _filtered = _w.Database.History.Changesets;
    //                 else
    //                     _filtered = _w.Database.History.Changesets.Where(cs =>
    //                         (cs.Tags & (1ul << (int)(_w.CurrentTag.Id - 1))) != 0).ToList();
    //                 break;
    //                 
    //         }
    //     }
    // }

    public void Update()
    {
        _listView.SetNeedsDisplay();
        // _listView.Source = new HistorySource(_w);
    }
    public void SetFiltering(MainWindow.FilteringMode filtering)
    {
        _listView.HistorySource.SetFiltering(filtering);
        if (_listView.TopItem >= _listView.HistorySource.Count && _listView.HistorySource.Count > 0)
        {
            _listView.SelectedItem = 0;
            _listView.TopItem = Math.Max(0, _listView.HistorySource.Count - 30);
        }
        _listView.SetNeedsDisplay();
    }
}
