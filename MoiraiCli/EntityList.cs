using System.Data;
using Terminal.Gui;

namespace Moirai;

public class EntityList : View
{
    private readonly MainWindow _w;
    private TableView _tableView;
    public EntityList(MainWindow w)
    {
        _w = w;
        ColorScheme = Colors.Base;
        _tableView = new TableView()
        {

            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Table = new DataTable()
            {
                Columns =
                {
                    new DataColumn("#", typeof(EntityId)),
                    new DataColumn("type", typeof(EntityTypeId)),
                    new DataColumn("name", typeof(string)),
                }
            },
        };
        _tableView.SelectedCellChanged += e => { _w.SelectEntity(e.NewRow); };
        // var typeStyle = _tableView.Style.GetOrCreateColumnStyle(_tableView.Table.Columns[1]);
        // typeStyle.ColorGetter = e =>
        // {
        //     e.
        //     return 
        // };
        Add(_tableView);
    }

    public void Load()
    {
        Update();
    }
    public void Update()
    {
        foreach (var entity in _w.Database.Entities.Skip(_tableView.Table.Rows.Count))
        {
            var row = _tableView.Table.Rows.Add();
            row[0] = entity.Id;
            row[1] = entity.GetProperty(Database.PropType).TypeId;
            row[2] = entity.GetProperty(Database.PropName).Value;
        }
        _tableView.Update();
    }
}