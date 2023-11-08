using System.Data;
using Moirai.Core;
using Terminal.Gui;

namespace Moirai;

public class EntityList : View
{
    private readonly MainWindow _w;
    private TableView _tableView;
    private Dictionary<int, (PropertyId, PropertyValue.ValueType)> _extraColumns = new();
    public void SetPropertyColumn(PropertyId id, bool displayed)
    {
        var name = _w.Database.GetPropertyName(id);
        if (displayed)
        {
            if (!_w.Database.GetPropertyType(id, out var type)) return;

            // Type t = type.BaseType switch
            // {
            //
            //     PropertyValue.ValueBaseType.String => typeof(String),
            //     PropertyValue.ValueBaseType.Ref => typeof(EntityId),
            //     PropertyValue.ValueBaseType.Number => typeof(long),
            //     PropertyValue.ValueBaseType.Bool => typeof(bool),
            //     PropertyValue.ValueBaseType.Enum => typeof(string),
            //     PropertyValue.ValueBaseType.EntityType => typeof(EntityTypeId),
            //     _ => throw new ArgumentOutOfRangeException()
            // };
            _extraColumns[_tableView.Table.Columns.Count] = (id, type);
            _tableView.Table.Columns.Add(new DataColumn(name, typeof(String)));
        }
        else
        {
            _tableView.Table.Columns.Remove(name);
        }
        Load();
    }
    public EntityList(MainWindow w)
    {
        _w = w;
        ColorScheme = Colors.TopLevel;
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
        _tableView.SelectedRow = -1;
        _tableView.SelectedCellChanged += e => { _w.SelectEntity(new EntityId(e.NewRow+1)); };
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
        _tableView.Table.Clear();
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
            for (int i = 3; i < _tableView.Table.Columns.Count; i++)
            {
                var pid = _extraColumns[i];
                var propertyValue = entity.GetProperty(pid.Item1);
                if(propertyValue != default)
                row[i] = _w.Database.Printer.Print(propertyValue, History.HistoryMode.Story);
                // switch (pid.Item2.BaseType)
                // {
                //     case PropertyValue.ValueBaseType.String:
                //         row[i] = propertyValue.Value;
                //         break;
                //     case PropertyValue.ValueBaseType.Ref:
                //         row[i] = propertyValue.Id;
                //         break;
                //     case PropertyValue.ValueBaseType.Number:
                //         row[i] = propertyValue.IntValue;
                //         break;
                //     case PropertyValue.ValueBaseType.Bool:
                //         row[i] = propertyValue.BoolValue;
                //         break;
                //     case PropertyValue.ValueBaseType.Enum:
                //         row[i] = _w.Database.Printer.Format() propertyValue.IntValue;
                //         break;
                //     case PropertyValue.ValueBaseType.EntityType:
                //         row[i] = ;
                //         break;
                //     default:
                //         throw new ArgumentOutOfRangeException();
                // }
            }
        }
        _tableView.Update();
    }
}