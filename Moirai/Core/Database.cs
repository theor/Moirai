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

    public readonly List<EnumDefinition> Enums = new() { default };
    public readonly List<EntityType> Types;
    public readonly List<PropertyDefinition> Properties = DefaultProperties();
    public readonly int BuiltinTypes;
    
    public readonly List<Action> Actions;
    public readonly List<Action> Events;
    
    public readonly StoryPrinter Printer;
    public History? History;
    public Changeset CurrentChangeset;
    
    private PredicateContext _ctx;
    
    private List<Entity> _entities = new() { default };
    public IEnumerable<Entity> Entities => _entities.Skip(1);

    public PredicateContext Ctx
    {
        set { _ctx = value; }
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
        Events = new();
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
            e.Properties[PropName.Id].Id  = PropName;
            e.Properties[PropName.Id].Value= name;
        }
        e.Id = new EntityId(_entities.Count);
        _entities.Add(e);
        // PerTypeIndices[(int)entityType.Id].Add(e.Id);
        CurrentChangeset.Changes?.Add(Change.Create(e.Id, entityType, name));

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
SET {GetPropertyName(property)} = {(value.Type.BaseType == PropertyValue.ValueBaseType.String ? ("'"+value.Value+"'") : (int)value.IntValue)}
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
        if (GetPropertyType(property, out var type) && type.BaseType == PropertyValue.ValueBaseType.Enum)
        {
            if (value.Type.BaseType != PropertyValue.ValueBaseType.Enum)
            {
                value = new PropertyValue { Type = Enums[type.Index].ValueType, IntValue = value.IntValue };
            }
        }
        var p = entity.Properties[property.Id];
        var prev = p.Value;
        p.Id = property;
        p.Value = value;
        CurrentChangeset.Changes.Add(Change.Set(entityId, property, prev, value));
        entity.Properties[property.Id] = p;
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
    public bool RunAction(Action action)
    {
        // Console.WriteLine($"[{action.Name}]");
        CurrentChangeset = new Changeset(action.Name, _ctx.Year);
        _ctx.ClearValueStack();
        // _ctx.Values.Clear();


        for (var index = 0; index < action.Effects.Count; index++)
        {
            var e = action.Effects[index];
            if (e is AssignPick { VariableIndex: -1 })
                throw new NotImplementedException("Arg index -1 on p " + index);

            if (!e.Execute(_ctx))
            {
                // Console.WriteLine($"  ABORT [{action.Name}]");
                if (CurrentChangeset.Changes.Count != 0)
                {
                    Console.Error.WriteLine("Action failed but left changes:");
                    History?.Changesets.Add(CurrentChangeset);
                }
                return false;
            }
        }
        _changedEntities.Clear();
        CurrentChangeset.GetAffectedEntities(_changedEntities);
        if (!String.IsNullOrEmpty(CurrentChangeset.Description) || CurrentChangeset.Changes.Any())
            History?.Changesets.Add(CurrentChangeset);

        RunEvents(_changedEntities);

        return true;
    }
    private void RunEvents(HashSet<EntityId> changedEntities)
    {
        foreach (var entity in changedEntities)
        {
            // Console.ForegroundColor = ConsoleColor.Yellow;
            // Console.WriteLine("Event entity: " + entity);
            // Console.ResetColor();
            foreach (var @event in Events)
            {

                using (var s = _ctx.RunScope())
                {
                    _ctx.SetArgument(0, entity);
                    if (@event.Whens.All(p => p.Value.IsTrue(_ctx)))
                    {
                        // Console.WriteLine("  @ " + @event.Name);
                        CurrentChangeset = new(@event.Name, _ctx.Year);
                        // using (var s2 = _ctx.RunScope())
                        {
                            foreach (var e in @event.Effects)
                            {
                                if (!e.Execute(_ctx))
                                {
                                    // continue;
                                    break;
                                }
                            }
                        }
                        if (CurrentChangeset.Changes.Any() || CurrentChangeset.HasDescription)
                            History?.Changesets?.Add(CurrentChangeset);
                    }
                }
            }
            // break;
        }

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
            case PropertyValue.ValueBaseType.Bool:
            case PropertyValue.ValueBaseType.Enum:
            case PropertyValue.ValueBaseType.EntityType:
                return "INTEGER DEFAULT 0";
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    public void Init()
    {
        Console.WriteLine(Path.GetFullPath("."));
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var cmd = _connection.CreateCommand();
        cmd.CommandText = $@"
CREATE TABLE entity (
    id INTEGER PRIMARY KEY,
    type INTEGER NOT NULL,
    {string.Join(",\n  ", Properties.Skip(3).Select(p => $@"{p.Name} {ToSqlType(p.Type)}"))}
);
CREATE INDEX types ON entity (type);
CREATE INDEX owners ON entity (owner) WHERE type = 3;
CREATE INDEX types_alive ON entity (type,alive) WHERE type = 2;
";
        cmd.ExecuteNonQuery();
        Profiler.Init(this);
        // PerTypeIndices = new List<EntityId>[Types.Count];
        // for (var i = 0; i < PerTypeIndices.Length; i++)
        // {
            // PerTypeIndices[i] = new List<EntityId>(100);
        // }
        foreach (Action a in Actions)
        {
            if (a.Filter is FilterAtStart)
                RunAction(a);
        }
    }
    public void Commit()
    {
        var path = "../../../hello.db";
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        using(var _backup = new SqliteConnection("Data Source=" + path))
            _connection.BackupDatabase(_backup);
    }
    public bool FindAll(IValue? predicate, ref List<EntityId> results)
    {
            results.Clear();
            if (predicate == null)
            {
                return false;
            }

            string sql = predicate.ToSql(_ctx);
            // Console.WriteLine(sql);
            var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
SELECT id FROM entity WHERE " + sql;
            // Console.WriteLine(cmd.CommandText);
            var r = cmd.ExecuteReader();
            while(r.Read())
                results.Add(new EntityId(r.GetInt64(0)));
            return true;
    }
}

