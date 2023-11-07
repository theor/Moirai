using System.Collections;
using NStack;
using Terminal.Gui;

namespace Moirai;

public class EntityDetailsView : FrameView
{
    private readonly ListView _listView;
    private readonly MainWindow _w;
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
        Add(_listView);
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
            _listView.Source = new PropertySource(e);
        }
    }

    public class PropertySource : IListDataSource
    {
        private readonly Entity _entity;
        public PropertySource(Entity entity)
        {
            _entity = entity;
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
                RenderUstr(driver, props.Id.ToString(), col, line, width, start);
            }
            else
            {
                if (!selected)
                    driver.SetAttribute(container.GetNormalColor());
                RenderUstr(driver,  props.Value.ToString(), col, line, width, start);
            }
        }
        public bool IsMarked(int item) => false;
        public void SetMark(int item, bool value)
        {
        }
        public IList ToList() => _entity.Properties.SelectMany(x => Enumerable.Repeat(x, 2)).ToList();
        public int Count => _entity.Properties.Count * 2;
        public int Length => 20;

        void RenderUstr(ConsoleDriver driver, ustring ustr, int col, int line, int width, int start = 0)
        {
            var u = TextFormatter.ClipAndJustify(ustr, width, TextAlignment.Left);
            driver.AddStr(u);
            width -= TextFormatter.GetTextWidth(u);
            while (width-- > 0)
            {
                driver.AddRune(' ');
            }
        }
    }
}