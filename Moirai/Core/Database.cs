using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Moirai;
using Moirai.Core;

public class Database
{
    public static readonly PropertyId PropId = new(1, default);
    public static readonly PropertyId PropType = new(2, default);
    public static readonly PropertyId PropName = new(3, default);
    public static readonly PropertyId PropYear = new(4, default);

    public static Database Instance = null!;

    public enum Frequency
    {
        PerXYear,
        EveryXYear,
    }

    public readonly List<FunctionDefinition> Functions = new() { default };

    public readonly List<EnumDefinition> Enums = new()
    {
        default,
        new EnumDefinition(new EnumDefinitionId(1), "Name", EntityNames.Names),
        EnumDefinition.FromEnum<Frequency>(new EnumDefinitionId(2)),
        // new EnumDefinition(new EnumDefinitionId(2), "Frequency", new List<string>{"Per","Every"}),
    };

    public EnumDefinition FrequencyEnumDefinition => Enums[2];

    public static readonly int BuiltinEnumCount = 3;
    public readonly List<EntityType> Types;
    public readonly int BuiltinTypes;

    public readonly List<EventTrigger> Actions;
    public readonly List<EventTrigger> Triggers;

    public readonly StoryPrinter Printer;
    public History? History;
    public Changeset CurrentChangeset;

    /// <summary>When set, <see cref="ExecuteContext.PassYears"/> allocates a fresh <see cref="ExecProfiler"/> per run.</summary>
    public bool ProfilingEnabled;

    /// <summary>Non-null while a profiled run is in progress (and afterwards, holding the last run's results).</summary>
    public ExecutionProfiler? ExecProfiler;

    private ExecuteContext _ctx;

    private List<Entity> _entities = new() { default };
    public IEnumerable<Entity> Entities => _entities.Skip(1);

    public ExecuteContext Ctx
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
            new("id", default, PropId.Id, PropertyValue.TypeRef),
            new("type", default, PropType.Id, PropertyValue.TypeEntityType),
            new PropertyDefinition("name", default, PropName.Id, PropertyValue.TypeString),
        };
    }

    public Database(ulong seed = 42)
    {
        Types = new List<EntityType>
        {
            new EntityType("default", 0),
            new EntityType("Time", 1).DeclareProperty("year", PropYear.Id,
                PropertyValue.TypeNumber)
        };
        BuiltinTypes = Types.Count;
        _ctx = new ExecuteContext(this, seed);
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
        Entity e = new(GetEntityType(entityType));

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


        var sql = @"INSERT INTO entity (default__name, default__type)
                    VALUES ($name, $type)
                    RETURNING default__id";
        if (!_commandsCreate.TryGetValue(sql, out var cmd))
        {
            cmd.Command = _connection.CreateCommand();
            cmd.Command.CommandText = sql;
            cmd.Name = cmd.Command.Parameters.Add("$name", SqliteType.Text);
            cmd.Type = cmd.Command.Parameters.Add("$type", SqliteType.Integer);
            cmd.Command.Prepare();
            _commandsCreate.Add(sql, cmd);
        }

        cmd.Name.Value = name ?? "?";
        cmd.Type.Value = entityType.Id;
        cmd.Command.ExecuteNonQuery();
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
        GetCalls++;
        var cmd = _connection.CreateCommand();
        cmd.CommandText =
            $@"SELECT {GetEntityTypeName(property.TypeId)}__{GetPropertyName(property)} FROM entity WHERE default__id = $id  LIMIT 1;";
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
        SetCalls++;
        // One prepared UPDATE per column ({Type}__{prop}); a column's type is fixed, so the bound
        // $v parameter's type is stable per cache entry. Value serialization matches the previous
        // inlined form exactly (string -> text, everything else -> IntValue, so null refs -> 0).
        string column = $"{GetEntityTypeName(property.TypeId)}__{GetPropertyName(property)}";
        if (!_commandsSet.TryGetValue(column, out var sc))
        {
            var c = _connection.CreateCommand();
            c.CommandText = $"UPDATE entity SET {column} = $v WHERE default__id = $id;";
            var vp = c.Parameters.Add("$v",
                value.Type.BaseType == PropertyValue.ValueBaseType.String ? SqliteType.Text : SqliteType.Integer);
            var ip = c.Parameters.Add("$id", SqliteType.Integer);
            c.Prepare();
            sc = new SetCommand(c, vp, ip);
            _commandsSet.Add(column, sc);
        }

        sc.Value.Value = value.Type.BaseType == PropertyValue.ValueBaseType.String
            ? (object)(value.Value ?? "")
            : (long)value.IntValue;
        sc.Id.Value = entityId.Id;
        sc.Command.ExecuteNonQuery();
        Profiler.Set(property);

        if (!TryGetEntity(entityId, out var entity))
            return false;

        if (property == PropId)
            throw new InvalidOperationException();
        if (property == PropType)
            throw new InvalidOperationException();

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
                    value = new PropertyValue(Enums[type.Index].ValueType, value.IntValue);
                }
            }
            else if (type.BaseType == PropertyValue.ValueBaseType.Percentage &&
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

    public PropertyId GetPropertyId(string typename, string name)
    {
        var t = GetEntityType(typename);
        return t.GetPropertyId(name);
        // for (var index = 1; index < Properties.Count; index++)
        // {
        //     var property = Properties[index];
        //     if (string.Equals(property.Name, name, StringComparison.InvariantCultureIgnoreCase))
        //         return new PropertyId((uint) index);
        // }

        return PropertyId.Null;
    }

    public string GetPropertyName(PropertyId prop)
    {
        return Printer.GetPropertyName(prop);
    }


    public EntityType? GetEntityType(PropertyValue.ValueType type)
    {
        if (type.BaseType != PropertyValue.ValueBaseType.EntityType && type.BaseType != PropertyValue.ValueBaseType.Ref)
            return default;
        return Types[type.Index];
    }

    public EntityType GetEntityType(EntityTypeId id)
    {
        return Types[(int)(id.Id)];
    }

    public EntityType GetEntityType(string typeName)
    {
        for (uint i = 1; i < Types.Count; i++)
        {
            if (Types[(int)i].Name == typeName)
                return Types[(int)i];
        }

        return Types[0];
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
        if (!pid.IsValid)
        {
            valueType = default;
            return false;
        }

        var t = GetEntityType(pid.TypeId);

        valueType = t.Properties[(int)pid.Id].Type;
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
        CurrentChangeset = new Changeset(History?.Changesets.Count ?? -1, eventTrigger.Name, _ctx.Year);
        _currentActionId = eventTrigger.Id;
        // _ctx.Values.Clear();

        var prof = ExecProfiler;
        var scope = prof?.Begin() ?? default;
        bool success = true;

        // NOT a using statement
        using (var s = _ctx.RunScope(false))
        {
            for (var index = 0; index < eventTrigger.Effects.Count; index++)
            {
                var e = eventTrigger.Effects[index];
                if (e is CallInstruction { Value: AssignPick { VariableIndex: -1 } })
                    throw new NotImplementedException("Arg index -1 on p " + index);

                if (!e.Execute(_ctx).BoolValue)
                {
                    // Console.WriteLine($"  ABORT [{action.Name}]");
                    // TODO option to keep empty changesets
                    // History?.Changesets.Add(CurrentChangeset);
                    if (CurrentChangeset.Changes.Count != 0)
                    {
                        Console.Error.WriteLine("Action failed but left changes:");
                    }

                    success = false;
                    break;
                }
            }

            if (success && CurrentChangeset.Changes.Any())
                History?.AddChangeset(CurrentChangeset);
        }

        // Record the event's own effect time (excludes the triggers fired below).
        prof?.RecordEvent(eventTrigger, scope, success);

        if (!success)
            return false;
        // _taggedEntities.Clear();
        // CurrentChangeset.GetTaggedEntities(_taggedEntities);

        // NEEDS to be run after the scope above is disposed
        // otherwise the value stack might still contain values from the event execution
        // which will then affect the sql generated, as it currently relies on the stack state to determine if a variable
        // needs to be computed or is part of a query
        // eg pick Item $2: ($2.owner = ...) owner might be computed instead of a sql var
        RunTriggers(CurrentChangeset);

        return true;
    }

    internal static readonly EntityId ChangePrevEntityId = new EntityId(uint.MaxValue - 1);
    internal static int EventAttemptCount;
    internal static int EventAttemptSuccess;

    private void RunTriggers(Changeset cs)
    {
        var prof = ExecProfiler;
        foreach (Changeset.Changed changed in cs.Changes)
        {
            _ctx.PrevEntity = changed.Prev;
            // Console.ForegroundColor = ConsoleColor.Yellow;
            // Console.WriteLine("Event entity: " + entity);
            // Console.ResetColor();
            foreach (var trigger in Triggers)
            {
                // if entity created but trigger is on change
                if (changed.Prev.Id.IsNull == (trigger.When.Item1 == EventTrigger.WhenType.Changed))
                    continue;
                // if (!@event.WhenTags.Contains(tag))
                //     continue;
                EventAttemptCount++;
                var scope = prof?.Begin() ?? default;
                bool matched = false;
                using (var s = _ctx.RunScope(false))
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
                            matched = true;
                            EventAttemptSuccess++;
                            // Console.WriteLine("  @ " + @event.Name);
                            CurrentChangeset = new(CurrentChangeset.Id, trigger.Name, _ctx.Year);
                            // using (var s2 = _ctx.RunScope())
                            {
                                foreach (var e in trigger.Effects)
                                {
                                    if (!e.Execute(_ctx).BoolValue)
                                    {
                                        // continue;
                                        break;
                                    }
                                }
                            }
                            if (CurrentChangeset.Changes.Any())
                                History?.AddChangeset(CurrentChangeset);
                        }
                    }
                }

                // An "attempt" mirrors EventAttemptCount (the when-type matched); "matched" means the
                // entity type + predicate matched and the trigger's effects ran.
                prof?.RecordTrigger(trigger, scope, matched);
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

        string indices = @"CREATE INDEX types ON entity (default__type);";
//         if (Properties.Any(p => p.Name == "owner"))
//             indices += @"
// CREATE INDEX owners ON entity (owner) WHERE type = 3;";
//         if (Properties.Any(p => p.Name == "alive"))
//             indices += @"
// CREATE INDEX types_alive ON entity (type,alive) WHERE type = 2;";

        cmd.CommandText = $@"
CREATE TABLE entity (
    default__id INTEGER PRIMARY KEY,
    default__type INTEGER NOT NULL,
    default__name TEXT,
    {string.Join(",\n  ", Types.Skip(1).SelectMany(t => t.Properties.Skip(4).Select(p => {
        // if (p.Type.BaseType == PropertyValue.ValueBaseType.Ref)
        // return $"FOREIGN KEY({p.Name}) REFERENCES entity(id)";
        return $@"{t.Name}__{p.Name} {ToSqlType(p.Type)}";
    })))}
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

        var timeType = GetEntityType("Time");
        if (_ctx.GetSingleton(timeType.Id, out var timeEntity) &&
            timeEntity.TryGetProperty(timeType.GetPropertyId("year"), out var year))
            _ctx.Year = year.IntValue;
    }

    public void Commit()
    {
        // save to disk fails on github runners
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            return;
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

    record struct CommandCreate(SqliteCommand Command, SqliteParameter Name, SqliteParameter Type);

    private Dictionary<string, SqliteCommand> _commandsPickRandom = new();
    private Dictionary<string, SqliteCommand> _commandsFindAll = new();
    private Dictionary<string, CommandCreate> _commandsCreate = new();

    record struct SetCommand(SqliteCommand Command, SqliteParameter Value, SqliteParameter Id);
    private Dictionary<string, SetCommand> _commandsSet = new();

    // TEMP diagnostics
    public static int PickCalls, PickPrepares, FindAllCalls, FindAllPrepares, SetCalls, GetCalls;
    public int PickCacheSize => _commandsPickRandom.Count;
    public int FindAllCacheSize => _commandsFindAll.Count;

    private static SqliteType SqliteTypeOf(PropertyValue.ValueBaseType t) => t switch
    {
        PropertyValue.ValueBaseType.String => SqliteType.Text,
        PropertyValue.ValueBaseType.Float => SqliteType.Real,
        _ => SqliteType.Integer,
    };

    /// <summary>
    /// Looks up (or prepares and caches) the command for <paramref name="sql"/>, then binds the
    /// parameters collected on <see cref="ExecuteContext.SqlParameters"/> during predicate compilation.
    /// The cache key is the parameterized SQL (query shape), so it no longer grows with simulation time.
    /// </summary>
    private SqliteCommand GetOrPrepare(Dictionary<string, SqliteCommand> cache, string sql, out bool prepared)
    {
        var ps = _ctx.SqlParameters;
        if (!cache.TryGetValue(sql, out var cmd))
        {
            prepared = true;
            cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < ps.Count; i++)
                cmd.Parameters.Add("$p" + i, SqliteTypeOf(ps[i].Type.BaseType));
            cmd.Prepare();
            cache.Add(sql, cmd);
        }
        else
        {
            prepared = false;
        }

        for (int i = 0; i < ps.Count; i++)
            cmd.Parameters[i].Value = ps[i].ToSqlParameter();
        return cmd;
    }

    public bool PickRandom(EntityTypeId entityTypeId, IValueSql? predicate, out EntityId id)
    {
        if (predicate == null && !entityTypeId.IsValid)
        {
            id = default;
            return false;
        }

        string? where, joins;
        _ctx.SqlParameters.Clear();
        (where, joins) = predicate.ToSql(_ctx);
        // (where, joins) = (null, null);
        if (string.IsNullOrEmpty(where))
            where = "entity.default__type = " + entityTypeId.Id;
        else
            where = $"entity.default__type = {entityTypeId.Id} AND {where}";
        var sql =
            $@"SELECT entity.default__id, rnd() as r FROM entity {(joins ?? "")} WHERE {where} ORDER BY r LIMIT 1";
        PickCalls++;
        var cmd = GetOrPrepare(_commandsPickRandom, sql, out var prepared);
        if (prepared) PickPrepares++;

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

    public bool FindAll(EntityTypeId entityTypeId, IValueSql? predicate, ref List<EntityId> results) =>
        FindAll(entityTypeId, predicate, ref results, out _);

    public bool FindAll(EntityTypeId entityTypeId, IValueSql? predicate, ref List<EntityId> results, out string? sql)
    {
        results.Clear();
        if (predicate == null && !entityTypeId.IsValid)
        {
            sql = null;
            return false;
        }

        string? where, joins;
        _ctx.SqlParameters.Clear();
        (where, joins) = predicate.ToSql(_ctx);
        if (string.IsNullOrEmpty(where))
            where = "entity.default__type = " + entityTypeId.Id;
        else
            where = $"entity.default__type = {entityTypeId.Id} AND {where}";
        sql = $@"SELECT entity.default__id FROM entity {(joins ?? "")} WHERE {where}";

        FindAllCalls++;
        var cmd = GetOrPrepare(_commandsFindAll, sql, out var prepared);
        if (prepared) FindAllPrepares++;

        // Console.WriteLine(sql);
        // Console.WriteLine(cmd.CommandText);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new EntityId((uint)r.GetInt32(0)));
        return true;
    }

    public List<string> Tags = new List<string> { null! };

    public bool DeclareTag(string tag)
    {
        if (Tags.IndexOf(tag) != -1)
        {
            return false;
        }

        Tags.Add(tag);
        return true;
    }
    
    public struct Record
    {
        public readonly string Text;
        public readonly int ChangesetId;
        public readonly int ActionId;
        public readonly long Year;

        public Record(string text, long year, int changesetId, int actionId)
        {
            Text = text;
            Year = year;
            ChangesetId = changesetId;
            ActionId = actionId;
        }
    }

    public List<Record> Records = new();
    private int _currentActionId;

    public void AppendRecord(string text, long year)
    {
        Records.Add(new(text, year, CurrentChangeset.Id, _currentActionId));
    }

    internal Dictionary<(EntityId, int), long> _marked = new();

    record struct MarkSqlCommand(SqliteCommand? Command, SqliteParameter Id, SqliteParameter Marker, SqliteParameter Year);

    private MarkSqlCommand _markSqlCommand;
    public void Mark(EntityId eId, int eventIndex)
    {
        _marked[(eId, eventIndex)] = _ctx.Year;

        if (_markSqlCommand.Command == null)
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = $@"
INSERT INTO marked (eid, marker, last_year) VALUES ($id,$marker,$year)
ON CONFLICT (eid, marker) DO UPDATE SET last_year = excluded.last_year, count = count + 1
;";
            // cmd.Parameters.AddWithValue("$p", GetPropertyName(property));
            var idParam = cmd.Parameters.Add("$id", SqliteType.Integer);
            var markerParam = cmd.Parameters.Add("$marker", SqliteType.Integer);
            var yearParam = cmd.Parameters.Add("$year", SqliteType.Integer);
            cmd.Prepare();
            _markSqlCommand = new(cmd, idParam, markerParam, yearParam);
        }
        
        _markSqlCommand.Id.Value = eId.Id;
        _markSqlCommand.Marker.Value = eventIndex;
        _markSqlCommand.Year.Value = _ctx.Year;
        _markSqlCommand.Command!.ExecuteNonQuery();
    }

    public bool GetLastMarked(EntityId eId, int eventIndex, out long year)
    {
        return _marked.TryGetValue((eId, eventIndex), out year);
    }


    public bool GetFunctionDefinition(string name, [NotNullWhen(true)] out FunctionDefinition? descriptor)
    {
        descriptor = Functions.FirstOrDefault(f => f.Name == name);
        return descriptor?.Name != null;
    }
}
