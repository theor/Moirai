using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Moirai;
using Moirai.Core;

public class Database
{
    public static readonly PropertyId PropId = new(1);
    public static readonly PropertyId PropType = new(2);
    public static readonly PropertyId PropName = new(3);
    public static readonly PropertyId PropYear = new(4);

    public static Database? Instance;

    public readonly List<EnumDefinition> Enums = new() { default, new EnumDefinition(new EnumDefinitionId(1), "Name", EntityNames.Names) };
    public static readonly int BuiltinEnumCount = 2;
    public readonly List<EntityType> Types;
    public readonly List<PropertyDefinition> Properties = DefaultProperties();
    public readonly int BuiltinTypes;

    public readonly List<EventTrigger> Actions;
    public readonly List<EventTrigger> Triggers;

    public readonly StoryPrinter Printer;
    public History? History;
    public Changeset CurrentChangeset;

    private PredicateContext _ctx;

    private List<Entity> _entities = new() { default };
    public IEnumerable<Entity> Entities => _entities.Skip(1);

    public PredicateContext Ctx
    {
        get { return _ctx; }
    }

    public string? FilePath { get; set; }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        IncludeFields = true,
        IgnoreReadOnlyProperties = true,

        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new EntityIdConverter(),
            new PropertyIdConverter(),
            new EntityTypeIdConverter(),
            new ValueTypeConverter(),
        }
    };

    private HashSet<EntityId> _changedEntities = new();

    public static List<PropertyDefinition> DefaultProperties()
    {
        return new()
        {
            default!,
            new("id", PropId.Id, PropertyValue.TypeRef),
            new("type", PropType.Id, PropertyValue.TypeEntityType),
            new PropertyDefinition("name", PropName.Id, PropertyValue.TypeString),
            new PropertyDefinition("year", PropYear.Id, PropertyValue.TypeString),
        };
    }

    public Database(ulong seed = 42)
    {
        Types = new List<EntityType> { default, new("Time", 1) };
        BuiltinTypes = Types.Count;
        _ctx = new PredicateContext(this, seed);
        Actions = new();
        Triggers = new();
        Printer = new StoryPrinter(this);
        Instance = this;
    }

    public void SetSeed(ulong seed)
    {
        _ctx.Rnd = new Pcg32(seed, seed);
    }


    public EntityId AllocateEntity(EntityTypeId entityType, string? name = null)
    {
        Entity e = new(this);

        e.Type = entityType;
        if (!String.IsNullOrEmpty(name))
        {
            e.SetProperty(PropName, name);
        }

        e.Id = new EntityId((uint)_entities.Count);
        _entities.Add(e);
        // PerTypeIndices[(int)entityType.Id].Add(e.Id);
        // TODO CS
        CurrentChangeset.RecordCreate(e);
        // CurrentChangeset.Changes?.Add(Change.Create(e.Id, entityType, name));

        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO entity (name, type)
                    VALUES ($name, $type)
                    RETURNING id";
        cmd.Parameters.AddWithValue("$name", name ?? "?");
        cmd.Parameters.AddWithValue("$type", entityType.Id);
        cmd.ExecuteScalar();
        // Console.WriteLine($"Result: " + cmd.ExecuteScalar());
        return e.Id;
    }

    // public List<EntityId>[] PerTypeIndices;
    public bool TryGetEntity(EntityId entityId, out Entity entity)
    {
        if (entityId.Id == 0 || entityId.Id >= _entities.Count)
        {
            entity = default;
            return false;
        }

        entity = _entities[(int)entityId.Id];
        return true;
    }

    public bool GetProperty(EntityId entityId, PropertyId property, out PropertyValue value)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = $@"SELECT {GetPropertyName(property)} FROM entity WHERE id = $id  LIMIT 1;";
        // cmd.Parameters.AddWithValue("$p", GetPropertyName(property));
        cmd.Parameters.AddWithValue("$id", entityId.Id);
        // cmd.Parameters.AddWithValue("$v",  value.Type.BaseType == PropertyValue.ValueBaseType.String ? value.Value : (int)value.IntValue);
        var res = cmd.ExecuteScalar();
        // Console.WriteLine($"get {GetPropertyName(property)} = {res}");
        if (!TryGetEntity(entityId, out var entity))
        {
            value = default;
            return false;
        }

        return entity.TryGetProperty(property, out value);
    }

    public bool SetProperty(EntityId entityId, PropertyId property, PropertyValue value = default)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = $@"UPDATE entity
SET {GetPropertyName(property)} = {(value.Type.BaseType == PropertyValue.ValueBaseType.String ? ("'" + value.Value + "'") : (int)value.IntValue)}
WHERE id = $id;";
        // cmd.Parameters.AddWithValue("$p", GetPropertyName(property));
        cmd.Parameters.AddWithValue("$id", entityId.Id);
        // cmd.Parameters.AddWithValue("$v",  value.Type.BaseType == PropertyValue.ValueBaseType.String ? value.Value : (int)value.IntValue);
        cmd.ExecuteNonQuery();
        Profiler.Set(property);

        if (!TryGetEntity(entityId, out var entity))
            return false;

        if (property == PropId)
            throw new NotImplementedException();
        if (property == PropType)
            throw new NotImplementedException();

        // if (entity.Properties == null)
        // {
        //     entity.Properties = new();
        //     _entities[(int)entityId.Id] = entity;
        // }
        if (GetPropertyType(property, out var type))
        {
            if (type.BaseType == PropertyValue.ValueBaseType.Enum)
            {
                if (value.Type.BaseType != PropertyValue.ValueBaseType.Enum)
                {
                    value = new PropertyValue(Enums[type.Index].ValueType,  value.IntValue);
                }
            } else if (type.BaseType == PropertyValue.ValueBaseType.Percentage &&
                       value.Type.BaseType != PropertyValue.ValueBaseType.Percentage)
                value = new PropertyValue(PropertyValue.TypePercent, value.FloatValue);
                
        } 

        PropertyValue prev = entity.SetProperty(property, value);
        
        // TODO CS
        CurrentChangeset.RecordSet(entity, property, prev);
        // CurrentChangeset.Changes.Add(Change.Set(entityId, property, prev, value));
        // for (var index = 0; index < entity.Properties.Count; index++)
        // {
        //     var entityProperty = entity.Properties[index];
        //     if (entityProperty.Id == property)
        //     {
        //
        //         var prev = entityProperty.Value;
        //         entityProperty.Value = value;
        //         entity.Properties[index] = entityProperty;
        //         CurrentChangeset.Changes.Add(Change.Set(entityId, property, prev, value));
        //         return true;
        //     }
        // }
        // CurrentChangeset.Changes.Add(Change.Set(entityId, property, default, value));
        // entity.Properties.Add(new Property(property, value));
        return true;
    }

    public PropertyId GetPropertyId(string name)
    {
        for (var index = 1; index < Properties.Count; index++)
        {
            var property = Properties[index];
            if (string.Equals(property.Name, name, StringComparison.InvariantCultureIgnoreCase))
                return new PropertyId((uint)index);
        }

        return PropertyId.Null;
    }

    public string GetPropertyName(PropertyId prop)
    {
        return Properties[(int)prop.Id].Name;
    }

    
    public EntityType GetEntityType(PropertyValue.ValueType type)
    {
        if (type.BaseType != PropertyValue.ValueBaseType.EntityType && type.BaseType != PropertyValue.ValueBaseType.Ref)
            return default;
        return Types[type.Index];
    }
    public EntityType GetEntityType(string typeName)
    {
        for (uint i = 1; i < Types.Count; i++)
        {
            if (Types[(int)i].Name == typeName)
                return Types[(int)i];
        }

        return default;
    }

    public string GetEntityTypeName(EntityTypeId typeId)
    {
        return Types[(int)typeId.Id].Name;
    }

    public bool GetEnumDefinition(string name, out EnumDefinition enumDefinition)
    {
        foreach (var definition in Enums)
        {
            if (definition.Name == name)
            {
                enumDefinition = definition;
                return true;
            }
        }

        enumDefinition = default;
        return false;
    }

    public bool GetPropertyType(PropertyId pid, out PropertyValue.ValueType valueType)
    {
        if (!pid.IsValid || pid.Id >= Properties.Count)
        {
            valueType = default;
            return false;
        }

        valueType = Properties[(int)pid.Id].Type;
        return true;
    }


    public bool RunAction(string actionName)
    {
        // Console.WriteLine($"[{actionName}]");
        foreach (var a in Actions)
        {
            if (a.Name == actionName)
            {
                return RunAction(a);
            }
        }

        return false;
    }

    public bool RunAction(EventTrigger eventTrigger)
    {
        // Console.WriteLine($"[{action.Name}]");
        CurrentChangeset = new Changeset(History?.Changesets.Count ?? -1, eventTrigger.Name, _ctx.Year, eventTrigger.Categories);
        _currentActionId = eventTrigger.Id;
        _ctx.ClearValueStack();
        // _ctx.Values.Clear();


        for (var index = 0; index < eventTrigger.Effects.Count; index++)
        {
            var e = eventTrigger.Effects[index];
            if (e is CallInstruction{ Value:  AssignPick { VariableIndex: -1 }})
                throw new NotImplementedException("Arg index -1 on p " + index);

            if (!e.Execute(_ctx))
            {
                // Console.WriteLine($"  ABORT [{action.Name}]");
                History?.Changesets.Add(CurrentChangeset);
                if (CurrentChangeset.Changes.Count != 0)
                {
                    Console.Error.WriteLine("Action failed but left changes:");
                }

                return false;
            }
        }
        if (CurrentChangeset.Changes.Any())
            History?.Changesets.Add(CurrentChangeset);

       
        // _taggedEntities.Clear();
        // CurrentChangeset.GetTaggedEntities(_taggedEntities);

        RunEvents(CurrentChangeset);

        return true;
    }

    internal static readonly EntityId ChangePrevEntityId = new EntityId(uint.MaxValue - 1);
    internal static int EventAttemptCount;
    internal static int EventAttemptSuccess;
    private void RunEvents(Changeset cs)
    {
        foreach (Changeset.Changed changed in cs.Changes)
        {
            _ctx.PrevEntity = changed.Prev;
            // Console.ForegroundColor = ConsoleColor.Yellow;
            // Console.WriteLine("Event entity: " + entity);
            // Console.ResetColor();
            foreach (var trigger in Triggers)
            {
                // if entity created but trigger is on change
                if(changed.Prev.Id.IsNull == (trigger.When.Item1 == EventTrigger.WhenType.Changed))
                    continue;
                // if (!@event.WhenTags.Contains(tag))
                //     continue;
                EventAttemptCount++;
                using (var s = _ctx.RunScope())
                {
                    
                    
                    if (trigger.When.Item2 == changed.New.Type)
                    {
                        // $old value
                        int varIdx = 0;
                    
                        if (trigger.When.Item1 == EventTrigger.WhenType.Changed)
                            _ctx.SetArgument(varIdx++, ChangePrevEntityId);
                        // $new value
                        _ctx.SetArgument(varIdx, changed.New.Id);
                        
                        if (trigger.When.Item3 == null || trigger.When.Item3.IsTrue(_ctx))
                        {
                            EventAttemptSuccess++;
                            // Console.WriteLine("  @ " + @event.Name);
                            CurrentChangeset = new(CurrentChangeset.Id, trigger.Name, _ctx.Year, trigger.Categories);
                            // using (var s2 = _ctx.RunScope())
                            {
                                foreach (var e in trigger.Effects)
                                {
                                    if (!e.Execute(_ctx))
                                    {
                                        // continue;
                                        break;
                                    }
                                }
                            }
                            if (CurrentChangeset.Changes.Any())
                                History?.Changesets?.Add(CurrentChangeset);
                        }
                    }
                }
            }
            // break;
        }

        _ctx.PrevEntity = default;
    }

    public string Serialize()
    {
        return JsonSerializer.Serialize(Entities, JsonSerializerOptions);
    }

    public void Deserialize(string json)
    {
        List<Entity> entities = JsonSerializer.Deserialize<List<Entity>>(json, JsonSerializerOptions);

        _entities = new() { default };
        _entities.AddRange(entities);
    }

    private SqliteConnection _connection;

    public static string ToSqlType(PropertyValue.ValueType t)
    {
        switch (t.BaseType)
        {
            case PropertyValue.ValueBaseType.String:
                return "TEXT";

            case PropertyValue.ValueBaseType.None:
            case PropertyValue.ValueBaseType.Ref:
            case PropertyValue.ValueBaseType.Number:
            case PropertyValue.ValueBaseType.Percentage:
            case PropertyValue.ValueBaseType.Bool:
            case PropertyValue.ValueBaseType.Enum:
            case PropertyValue.ValueBaseType.EntityType:
                return "INTEGER DEFAULT 0";
            case PropertyValue.ValueBaseType.Float:
                return "REAL DEFAULT 0";
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Init()
    {
        Console.WriteLine(Path.GetFullPath("."));
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.CreateFunction("rnd", () => _ctx.Rnd.GenerateNext());
        _connection.Open();
        var cmd = _connection.CreateCommand();

        string indices = @"CREATE INDEX types ON entity (type);";
        if (Properties.Any(p => p.Name == "owner"))
            indices += @"
CREATE INDEX owners ON entity (owner) WHERE type = 3;";
        if (Properties.Any(p => p.Name == "alive"))
            indices += @"
CREATE INDEX types_alive ON entity (type,alive) WHERE type = 2;";

        cmd.CommandText = $@"
CREATE TABLE entity (
    id INTEGER PRIMARY KEY,
    type INTEGER NOT NULL,
    {string.Join(",\n  ", Properties.Skip(3).Select(p => {
        // if (p.Type.BaseType == PropertyValue.ValueBaseType.Ref)
            // return $"FOREIGN KEY({p.Name}) REFERENCES entity(id)";
        return $@"{p.Name} {ToSqlType(p.Type)}";
    }))}
);
{indices}
CREATE TABLE marked (
    eid INTEGER NOT NULL,
    marker INTEGER NOT NULL,
    last_year INTEGER NOT NULL,
    count  INTEGER DEFAULT 1, PRIMARY KEY(eid, marker))
";
        cmd.ExecuteNonQuery();
        Profiler.Init(this);
        // PerTypeIndices = new List<EntityId>[Types.Count];
        // for (var i = 0; i < PerTypeIndices.Length; i++)
        // {
        // PerTypeIndices[i] = new List<EntityId>(100);
        // }
        foreach (EventTrigger a in Actions)
        {
            if (a.Filter is FilterAtStart)
                RunAction(a);
        }
    }

    public void Commit()
    {
        var path = "hello.db";
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        using (var _backup = new SqliteConnection("Data Source=" + path))
            _connection.BackupDatabase(_backup);
    }

    private Dictionary<string, SqliteCommand> _commands = new();
    private Dictionary<string, SqliteCommand> _commands2 = new();
    public bool PickRandom(IValue value, out EntityId id)
    {
        var(where, joins) = value.ToSql(_ctx);
        var sql = $@"SELECT id, rnd() as r FROM entity {(joins ?? "")} WHERE {where} ORDER BY r LIMIT 1";
        if (!_commands.TryGetValue(sql, out var cmd))
        {
            // Console.WriteLine(sql);
            cmd = _connection.CreateCommand();
            // cmd.CommandText = $@"SELECT id FROM entity WHERE {sql} LIMIT 1";
            cmd.CommandText = sql;
            cmd.Prepare();
            _commands.Add(sql, cmd);
        }

        // Console.WriteLine(cmd.CommandText);
        var r = cmd.ExecuteScalar();
        if (r is long u)
        {
            id = new EntityId((uint)u);
            return true;
        }
            id = default;
        return false;
    }
    public bool FindAll(IValue? predicate, ref List<EntityId> results)
    {
        results.Clear();
        if (predicate == null)
        {
            return false;
        }
        var (where, joins) = predicate.ToSql(_ctx);
        var sql = $@"SELECT id FROM entity {(joins ?? "")} WHERE {where}";

        if (!_commands2.TryGetValue(sql, out var cmd))
        {
            // Console.WriteLine(sql);
            cmd = _connection.CreateCommand();
            // cmd.CommandText = $@"SELECT id FROM entity WHERE {sql} LIMIT 1";
            cmd.CommandText = sql;
            cmd.Prepare();
            _commands2.Add(sql, cmd);
        }
        // Console.WriteLine(sql);
        // Console.WriteLine(cmd.CommandText);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new EntityId((uint)r.GetInt32(0)));
        return true;
    }

    public List<string> Tags = new List<string> { null! };
    public List<string> Categories = new List<string> { null! };

    public bool DeclareTag(string tag)
    {
        if (Tags.IndexOf(tag) != -1)
        {
            return false;
        }

        Tags.Add(tag);
        return true;
    }

    public CategoryId GetCategoryId(string cat)
    {
        int index = Categories.IndexOf(cat);
        if (index == -1)
        {
            Categories.Add(cat);
            return new CategoryId((ulong)(Categories.Count - 1));
        }


        return new CategoryId((ulong)index);
    }

    public string GetCategoryName(CategoryId tagId)
    {
        return Categories[(int)tagId.Id];
    }

    public struct Record
    {
        public readonly string Text;
        public readonly int ChangesetId;
        public readonly int ActionId;
        public readonly long Year;
        public readonly ulong Categories;

        public Record(string text, long year, ulong categories, int changesetId, int actionId)
        {
            Text = text;
            Year = year;
            ChangesetId = changesetId;
            ActionId = actionId;
            Categories = categories;
        }
    }

    public List<Record> Records = new();
    private int _currentActionId;

    public void AppendRecord(string text, long year, ulong categories)
    {
        Records.Add(new(text, year, categories, CurrentChangeset.Id, _currentActionId));
    }
    internal Dictionary<(EntityId, int), long> _marked = new();

    public void Mark(EntityId eId, int eventIndex)
    {
        _marked[(eId, eventIndex)] = _ctx.Year;
        var cmd = _connection.CreateCommand();
        cmd.CommandText = $@"
INSERT INTO marked (eid, marker, last_year) VALUES ($id,$marker,$year)
ON CONFLICT (eid, marker) DO UPDATE SET last_year = excluded.last_year, count = count + 1
;";
        // cmd.Parameters.AddWithValue("$p", GetPropertyName(property));
        cmd.Parameters.AddWithValue("$id", eId.Id);
        cmd.Parameters.AddWithValue("$marker", eventIndex);
        cmd.Parameters.AddWithValue("$year", _ctx.Year);
        // cmd.Parameters.AddWithValue("$v",  value.Type.BaseType == PropertyValue.ValueBaseType.String ? value.Value : (int)value.IntValue);
        cmd.ExecuteNonQuery();
    }

    public bool GetLastMarked(EntityId eId, int eventIndex, out long year)
    {
        return _marked.TryGetValue((eId, eventIndex), out year);
    }
}
