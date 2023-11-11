using Terminal.Gui;

namespace Moirai;

public class TagListView : FrameView
{
    private readonly MainWindow _w;
    private readonly ListView _listView;

    public TagListView(MainWindow w)
    {
        _w = w;
        _listView = new ListView
        {
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMultipleSelection = true,
        };
        _listView.OpenSelectedItem += e =>
        {
            _w.SelectTag(new TagId((ulong)e.Item));
            _w.SetFiltering(MainWindow.FilteringMode.Tag);
        };
        this.Add(_listView);
    }

    public void Load()
    {
        _listView.Source = new ListSource<string>(_w.Database.Tags, 
            (ListView view, ConsoleDriver driver, bool selected, int item, int col, int line, int width, int start) =>
            {
                driver.AddStr(item == 0 ? "-" : _w.Database.GetTagName(new TagId((ulong)item)));
            },
            i => _w.CurrentTag.Id == (ulong)i);
    }
}
public class CatListView : FrameView
{
    private readonly MainWindow _w;
    private readonly ListView _listView;

    public CatListView(MainWindow w)
    {
        _w = w;
        _listView = new ListView
        {
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMultipleSelection = true,
        };
        _listView.OpenSelectedItem += e =>
        {
            _w.SelectCategory(new CategoryId((ulong)e.Item));
            _w.SetFiltering(MainWindow.FilteringMode.Category);
        };
        this.Add(_listView);
    }
    public void Load()
    {
        _listView.Source = new ListSource<string>(_w.Database.Categories, 
            (ListView view, ConsoleDriver driver, bool selected, int item, int col, int line, int width, int start) =>
            {
                driver.AddStr(item == 0 ? "-" : _w.Database.GetCategoryName(new CategoryId((ulong)item)));
            },
            i => _w.CurrentCategory.Id == (ulong)i);
    }
}
