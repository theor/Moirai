using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            default!, new("id", PropId.Id, PropertyValue.TypeRef),
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
        Entity e = new();

        e.Properties = new();
        e.Properties.Add(new Property(PropType, entityType));
        if (!String.IsNullOrEmpty(name))
            e.Properties.Add(new Property(PropName, name));
        e.Id = new EntityId(_entities.Count);
        _entities.Add(e);
        CurrentChangeset.Changes?.Add(Change.Create(e.Id, entityType, name));

        return e.Id;
    }
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
        if (!TryGetEntity(entityId, out var entity))
        {
            value = default;
            return false;
        }
        return entity.TryGetProperty(property, out value);
    }
    public bool SetProperty(EntityId entityId, PropertyId property, PropertyValue value = default)
    {
        Profiler.Set(property);

        if (!TryGetEntity(entityId, out var entity))
            return false;

        if (property == PropId)
            throw new NotImplementedException();

        if (entity.Properties == null)
        {
            entity.Properties = new();
            _entities[(int)entityId.Id] = entity;
        }
        if (GetPropertyType(property, out var type) && type.BaseType == PropertyValue.ValueBaseType.Enum)
        {
            if (value.Type.BaseType != PropertyValue.ValueBaseType.Enum)
            {
                value = new PropertyValue { Type = Enums[type.Index].ValueType, IntValue = value.IntValue };
            }
        }
        for (var index = 0; index < entity.Properties.Count; index++)
        {
            var entityProperty = entity.Properties[index];
            if (entityProperty.Id == property)
            {

                var prev = entityProperty.Value;
                entityProperty.Value = value;
                entity.Properties[index] = entityProperty;
                CurrentChangeset.Changes.Add(Change.Set(entityId, property, prev, value));
                return true;
            }
        }
        CurrentChangeset.Changes.Add(Change.Set(entityId, property, default, value));
        entity.Properties.Add(new Property(property, value));
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
        CurrentChangeset = new Changeset(action.Name, _ctx._year);
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
                        CurrentChangeset = new(@event.Name, _ctx._year);
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
                        if (CurrentChangeset.Changes.Any())
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
    public void Init()
    {
        Profiler.Init(this);
        foreach (Action a in Actions)
        {
            if (a.Filter is FilterAtStart)
                RunAction(a);
        }
    }
}

