using System.Diagnostics;
using Pcg;
using Pcg.Core;

public readonly struct PropertyId : IEquatable<PropertyId>
{
    public bool Equals(PropertyId other)
    {
        return Id == other.Id;
    }
    public override bool Equals(object? obj)
    {
        return obj is PropertyId other && Equals(other);
    }
    public override int GetHashCode()
    {
        return (int)Id;
    }
    public static bool operator ==(PropertyId left, PropertyId right)
    {
        return left.Equals(right);
    }
    public static bool operator !=(PropertyId left, PropertyId right)
    {
        return !left.Equals(right);
    }
    public static readonly PropertyId Null = new PropertyId();
    public bool IsValid => Id != 0;
    public readonly uint Id;
    public PropertyId(uint id)
    {
        Id = id;
    }
}

public readonly struct EnumDefinition
{
    public readonly string Name;
    public readonly List<string> Values;
    public readonly ushort Index;
    public PropertyValue.ValueType ValueType => PropertyValue.TypeEnum(Index);
    public EnumDefinition(ushort index, string name, List<string> values)
    {
        Name = name;
        Values = values;
        Index = index;
    }
    public bool GetValueFromName(string valueName, out PropertyValue propertyValue)
    {
        for (int i = 0; i < Values.Count; i++)
        {
            if (Values[i] == valueName)
            {
                propertyValue = new PropertyValue { IntValue = i, Type = ValueType };
                return true;
            }
        }
        propertyValue = default;
        return false;
    }
    public PropertyValue GetRandomValue(Pcg32 rnd)
    {
        var i = rnd.GenerateNext((uint)Values.Count);
        return new PropertyValue { IntValue = i, Type = ValueType };
    }
}
public readonly struct EntityType
{
    public readonly string Name;
    public readonly EntityTypeId Id;
    public EntityType(string name, uint id)
    {
        Name = name;
        Id = new EntityTypeId(id);
    }
}
public readonly struct EntityTypeId : IEquatable<EntityTypeId>
{
    public readonly uint Id;
    public static readonly EntityTypeId Null = new EntityTypeId(0);
    public EntityTypeId( uint id)
    {
        Id = id;
    }
    public bool IsValid => Id != 0;

    public bool Equals(EntityTypeId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is EntityTypeId other && Equals(other);
    public override int GetHashCode() => (int)Id;
    public static bool operator ==(EntityTypeId left, EntityTypeId right) => left.Equals(right);
    public static bool operator !=(EntityTypeId left, EntityTypeId right) => !left.Equals(right);
}
public readonly struct PropertyDefinition {
public readonly string Name;
public readonly uint Id;
public readonly PropertyValue.ValueType Type;
public PropertyDefinition(string name, uint id, PropertyValue.ValueType type)
{
    Name = name;
    Id = id;
    Type = type;
}
}
public class Database
{
    public static readonly PropertyId PropId = new PropertyId(1);
    public static readonly PropertyId PropType = new PropertyId(2);
    public static readonly PropertyId PropName = new PropertyId(3);
    public static List<PropertyDefinition> DefaultProperties()
    {

        return new() { default!, new("id", 1, PropertyValue.TypeRef),
            new("type", 2, PropertyValue.TypeEntityType),
            new PropertyDefinition("name", 3, PropertyValue.TypeString), };
    }
    public List<PropertyDefinition> Properties = DefaultProperties();
    private List<Entity> _entities = new() { default };
    internal List<Rule> Rules = new();
    public List<Action> Actions = new();
    public List<EntityType> Types = new(){default};
    public List<EnumDefinition> Enums = new(){default};
    public readonly List<Action> Events;

    public History? History;
    public Changeset CurrentChangeset;

    private PredicateContext _ctx;
  
    public IEnumerable<Entity> Entities => _entities.Skip(1);

    // public int DeclareProperty(string name)
    // {
    //     Properties.Add(name);
    //     return Properties.Count - 1;
    // }

    public EntityId AllocateEntity(EntityTypeId entityType, string? name = null)
    {
        Entity e = new();

        e.Properties = new();
        e.Properties.Add(new Property(PropType, entityType));
        if(name != null)
            e.Properties.Add(new Property(PropName, name));
        e.Id = new EntityId((long)_entities.Count);
        _entities.Add(e);
        CurrentChangeset.Changes?.Add(Change.Create(e.Id, entityType, name));

        return e.Id;
    }
    public void AddEntity(ref Entity e)
    {
        Debug.Assert(e.Id.IsNull);
        e.Id = new EntityId((long)_entities.Count);
        _entities.Add(e);
    }
    // private bool CheckEntity(in Entity entity)
    // {
    //     bool res = true;
    //     _ctx.EntityId = entity.Id;
    //     foreach (var rule in Rules)
    //     {
    //         if (rule.If.IsTrue(_ctx))
    //         {
    //             var isTrue = rule.Then.IsTrue(_ctx);
    //             res = res && isTrue;
    //             // _logger.LogRule(isTrue, rule);
    //         }
    //     }
    //     return true;
    // }
    public bool TryGetEntity(long entityId, out Entity entity)
    {
        if (!EntityExists(entityId))
        {
            entity = default;
            return false;
        }
        entity = _entities[(int)entityId];
        return true;
    }
    public bool EntityExists(long entityId)
    {

        return entityId != 0 && entityId < (long)_entities.Count;
    }


    public struct EntityScope : IDisposable
    {
        public readonly Database Database;
        public long EntityId;
        public Entity Entity;
        public EntityScope(Database database, long entityId, Entity entity)
        {
            Database = database;
            EntityId = entityId;
            Entity = entity;
        }
        public void Dispose()
        {
            Database._entities[(int)EntityId] = Entity;
        }
    }

    public EntityScope GetEntityScope(long entityId) => new EntityScope(this, entityId, _entities[(int)entityId]);
    // public bool SetProperty(PropertyValue entityId, PropertyType property, PropertyValue value = default)
    // {
    //     return SetProperty(entityId.IntValue, property, value);
    // }
    public bool SetProperty(long entityId, PropertyId property, PropertyValue value = default)
    {
        if (!TryGetEntity(entityId, out var entity))
            return false;

        if (property == PropId)
            throw new NotImplementedException();

        if (entity.Properties == null)
        {
            entity.Properties = new();
            _entities[(int)entityId] = entity;
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
            if (entityProperty.Type == property)
            {
                
                var prev = entityProperty.Value;
                entityProperty.Value = value;
                entity.Properties[index] = entityProperty;
                CurrentChangeset.Changes?.Add(Change.Set(new EntityId(entityId), property, prev, value));
                return true;
            }
        }
        CurrentChangeset.Changes?.Add(Change.Set(new EntityId(entityId), property, default, value));
        entity.Properties.Add(new Property(property, value));
        return true;
    }
    public bool RunAction(string actionName)
    {
        Console.WriteLine($"[{actionName}]");
        foreach (var a in this.Actions)
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
        Console.WriteLine($"[{action.Name}]");
        CurrentChangeset = new Changeset(action.Name);
        _ctx.Values.Clear();
        // int argCount = 0;
        // for (var index = 0; index < effect.Effects.Count; index++)
        // {
        //     var e = effect.Effects[index];
        //     if (e is PredicateParameter pp)
        //     {
        //         pp.ArgumentIndex = argCount++;
        //         effect.Effects[index] = pp;
        //     }
        // }

        for (var index = 0; index < action.Effects.Count; index++)
        {
            var e = action.Effects[index];
            if (e is AssignPick { VariableIndex: -1 })
                throw new System.NotImplementedException("Arg index -1 on p " + index);

            if (!e.Execute(_ctx))
            {
                if (CurrentChangeset.Changes.Count != 0)
                {
                    Console.Error.WriteLine("Action failed but left changes:");
                    History?.Changesets?.Add(CurrentChangeset);
                }
                return false;
            }
        }
        HashSet<EntityId> changedEntities = new();
        
        foreach (var change in CurrentChangeset.Changes)
        {
            changedEntities.Add(change.EntityId);
            if (change.PrevValue.Type == PropertyValue.TypeRef && !change.PrevValue.Id.IsNull)
                changedEntities.Add(change.PrevValue.Id);
            if (change.NewValue.Type == PropertyValue.TypeRef && !change.NewValue.Id.IsNull)
                changedEntities.Add(change.NewValue.Id);
        }
        History?.Changesets?.Add(CurrentChangeset);
        RunEvents(changedEntities);

        return true;
        // if (_ctx.Query(effect.If, out var v))
        // {
        //     _ctx.EntityId = v.IntValue;
        //     return effect.Then.MakeTrue(_ctx);
        // }
        // return false;
    }
    private void RunEvents(HashSet<EntityId> changedEntities)
    {
        foreach (var entity in changedEntities)
        {
            while (_ctx.PopArgument() > 0){}
            // TODO BUG
            _ctx.PushArgument(entity);
            foreach (var @event in Events)
            {
                if(@event.Whens.All(p => p.Value.IsTrue(_ctx)))
                {
                    CurrentChangeset = new(@event.Name);
                    
                        if(!@event.Effects.All(e => e.Execute(_ctx)))
                            continue;
                    History?.Changesets?.Add(CurrentChangeset);
                }
            }
        }

    }
    public void AppendDescription(string? desc)
    {
        if (!String.IsNullOrEmpty(desc))
        {
            if(!String.IsNullOrEmpty(CurrentChangeset.Description))
                CurrentChangeset.Description += "\n";
            CurrentChangeset.Description += desc;
        }
    }
    public string Format(InterpolatedString formatAction)
    {
        var printer = new StoryPrinter(this);
        var propertyValues = formatAction.Arguments.Select(path => printer.Print(path.Compute(_ctx))).Cast<object?>().ToArray();
        return String.Format(formatAction.FormatString, propertyValues);
    }
    private static ConsoleColor[] Colors = { ConsoleColor.Cyan, ConsoleColor.Magenta, ConsoleColor.Green, ConsoleColor.Yellow };
    public StoryPrinter Printer;
    public Database(ulong seed = 42)
    {
        _ctx = new PredicateContext(this, seed);
        Actions = new();
        Events = new();
        Printer = new StoryPrinter(this);
    }
    public void PrintDb()
    {
        // Console.WriteLine("[DB]");
        var printer = new StoryPrinter(this);
        bool any = false;
        foreach (var e in Entities)
        {
            any = true;
            string name = e.TryGetProperty(Database.PropType, out var nameprop) ? (nameprop.Value ?? "") : "";
            Console.ForegroundColor = Colors[e.GetProperty(Database.PropType).IntValue % Colors.Length];
            Console.WriteLine($"e{e.Id} {name}");
            Console.ResetColor();
            if (e.Properties != null)
                foreach (var property in e.Properties)
                {
                    if (property.Type != Database.PropType)
                        Console.WriteLine($"  {Properties[(int)property.Type.Id].Name}: {printer.Print(property.Value, property.Type)}");
                        // Console.WriteLine($"  {FormatProperty(property)}");
                }
        }
        if (!any)
            Console.WriteLine("<Empty>");
        Console.WriteLine();
    }
    public PropertyId GetProperty(string name)
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
            if(Types[(int)i].Name == typeName)
                return Types[(int)i];
        }
        return default;
    }
    public string GetEntityTypeName(EntityTypeId typeId)
    {
        return Types[(int)typeId.Id].Name;
    }
    public void SetSeed(ulong seed)
    {
        _ctx.Rnd = new Pcg32(seed, seed);
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
    public bool GetPropertyType(PropertyId type, out PropertyValue.ValueType valueType)
    {
        if (!type.IsValid || type.Id >= Properties.Count)
        {
            valueType = default;
            return false;
        }
        valueType = Properties[(int)type.Id].Type;
        return true;
    }
    public void PrintHistory()
    {
        foreach (var cs in History.Changesets)
        {
            Printer.PrintChangeset(cs);
        }
    }
}

