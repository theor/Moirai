using System.Collections;
using NStack;
using Terminal.Gui;

namespace Moirai;

public class EntityDetailsView : FrameView
{
    private readonly ListView _listView;
    private readonly MainWindow _w;
    private HashSet<PropertyId> _displayedProps = new();
    public EntityDetailsView(MainWindow w) : base("Details")
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
            switch (e.Value)
            {
                case PropertyId id:
                {
                    if(_displayedProps.Add(id))
                        _w.SetDisplayedProperty(id, true);
                    else
                    {
                        _displayedProps.Remove(id);
                        
                        _w.SetDisplayedProperty(id, false);
                    }
                    break;
                }
                case PropertyValue p:
                {
                    if (p.Type == PropertyValue.TypeRef)
                    {
                        _w.SelectEntity(p.Id);
                    }
                    break;
                }
            }
        };
        Add(_listView);
        var checkBox = new CheckBox("Filter"){ Y = Pos.AnchorEnd(1), X = Pos.AnchorEnd(10)};
        checkBox.Toggled += _ => _w.SetFiltering(checkBox.Checked);
        Add(checkBox);
    }

    public void SetSelectedEntity(EntityId eid)
    {
        if (!_w.Database.TryGetEntity(eid, out var e))
        {
            Title = "No entity selected";
            _listView.Source = null;
        }
        else
        {
            Title = eid.ToString();
            // _listView.SetSource(e.Properties.Select(p => p.Id.ToString() + p.Value.ToString()).ToList());
            _listView.Source = new PropertySource(_w.Database, e, _displayedProps);
        }
    }

    public class PropertySource : IListDataSource
    {
        private readonly Database _database;
        private readonly Entity _entity;
        private readonly HashSet<PropertyId> _displayedProps;
        public PropertySource(Database database, Entity entity, HashSet<PropertyId> displayedProps)
        {
            _database = database;
            _entity = entity;
            _displayedProps = displayedProps;
        }
        public void Render(ListView container, ConsoleDriver driver, bool selected, int item, int col, int line, int width, int start = 0)
        {
            container.Move(col, line);
            int propIdx = item / 2;
            bool isHeader = item % 2 == 0;
            var props = _entity.Properties[propIdx];
            if (isHeader)
            {
                if (!selected)
                    driver.SetAttribute(container.GetHotNormalColor());
                RenderUstr(driver, props.Id.ToString(), col, line, width, _displayedProps.Contains(props.Id));
            }
            else
            {
                if (!selected)
                    driver.SetAttribute(container.GetNormalColor());
                var print = " " +_database.Printer.Print(props.Value);
                if (props.Value.Type.BaseType == PropertyValue.ValueBaseType.Ref && !props.Value.Id.IsNull &&
                    _database.GetProperty(props.Value.Id, Database.PropName, out var name))
                    print += " " + name.Value;
                RenderUstr(driver, print, col, line, width, null);
            }
        }
        public bool IsMarked(int item) => false;
        public void SetMark(int item, bool value)
        {
        }
        public IList ToList() => _entity.Properties.SelectMany(x => new object[]{x.Id, x.Value}).ToList();
        public int Count => _entity.Properties.Length * 2;
        public int Length => 20;

        void RenderUstr(ConsoleDriver driver, ustring ustr, int col, int line, int width, bool? enabled)
        {
            var u = TextFormatter.ClipAndJustify(ustr, width, TextAlignment.Left);
            driver.AddStr(u);
            width -= TextFormatter.GetTextWidth(u);
            while (width-- > 1)
            {
                driver.AddRune(' ');
            }
            if(enabled.HasValue)
                driver.AddRune(enabled.Value ? driver.Checked : driver.UnChecked);
            else
                driver.AddRune(' ');
        }
    }
}