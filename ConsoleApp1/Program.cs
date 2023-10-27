// See https://aka.ms/new-console-template for more information

using System.Collections;
using System.Diagnostics;

internal class Program
{
    public static void PrintDb(Database db)
    {
        bool any = false;
        foreach (var e in db.Entities)
        {
            any = true;
            Console.WriteLine($"e{e.Id}");
            if(e.Properties != null)
                foreach (var property in e.Properties)
                {
                    Console.WriteLine($"  {FormatProperty(property)}");
                }
        }
        if(!any)
            Console.WriteLine("<Empty>");
    }
    private static string FormatProperty(Property property)
    {
        switch (property.Type)
        {

            case PropertyType.Type:
                switch (property.Value.IntValue)
                {
                    case Properties.TypePerson: return "Type: Person";
                    case Properties.TypeItem: return "Type: Item";
                    default: return "Type UNKNOWN";
                }
            case PropertyType.Alive:
                return property.Value.BoolValue ? "Alive" : "Dead";
            case PropertyType.Owner:
                return $"Owner: {property.Value.IntValue}";
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    public static void Main(string[] args)
    {
        // Console.WriteLine("Hello, World!");
        // var rules = new List<Rule>
        // {
        //     new Rule(new Property(PropertyType.Type, "Person"))
        // };
        var db = new Database
        {
            Rules =
            {
                new Rule("Persons have liveliness", new PropertyEquals(PropertyType.Type, Properties.TypePerson), new HasProperty(PropertyType.Alive)),
                new Rule("Items have owners", new PropertyEquals(PropertyType.Type, Properties.TypeItem), new HasProperty(PropertyType.Owner)),
            },
            Effects =
            {
              new Rule("Create person", new True(), new And(new EntityExists(), new PropertyEquals(PropertyType.Type, Properties.TypePerson), new PropertyEquals(PropertyType.Alive, true))),
              new Rule("Alive people can die",
                  new And(new PropertyEquals(PropertyType.Type, Properties.TypePerson), new PropertyEquals(PropertyType.Alive, true)),
                  new PropertyEquals(PropertyType.Alive, false)),
            },
        };
        PrintDb(db);
        db.RunEffect(db.Effects[0]);
        PrintDb(db);

    }
}



internal enum PropertyType
{
    Type,
    Alive,
    Owner
}
// item.owner: x -> y gifted, stolen or inherited
// owner dies -> owned items have no owners

// generator : create fact


// rules:
// alive -> dies
// owner alive, item owned -> lost, given, stolen
// owner dead, item owned -> 
static class Properties
{
    public const int TypePerson = 1;
    public const int TypeItem = 2;
}

class Logger
{
    
}
class Database
{
    private List<Entity> _entities = new() { default };
    public List<Rule> Rules = new();
    private Logger _logger = new();
    public List<Rule> Effects = new();

    private PredicateContext _ctx;
    public Database()
    {
        _ctx = new PredicateContext(this);
    }
    public IEnumerable<Entity> Entities => _entities.Skip(1);

    public long AllocateEntity()
    {
        Entity e = new();
        e.Id = (long)_entities.Count;
        _entities.Add(e);
        return e.Id;
    }
    public void AddEntity(ref Entity e)
    {
        Debug.Assert(e.Id == 0);
        e.Id = (long)_entities.Count;
        _entities.Add(e);
    }
    private bool CheckEntity(in Entity entity)
    {
        bool res = true;
        _ctx.EntityId = entity.Id;
        foreach (var rule in Rules)
        {
            if (rule.If.IsTrue(_ctx))
            {
                var isTrue = rule.Then.IsTrue(_ctx);
                res = res && isTrue;
                // _logger.LogRule(isTrue, rule);
            }
        }
        return true;
    }
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


    internal struct EntityScope : IDisposable
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
    public bool SetProperty(long entityId, PropertyType property, PropertyValue value = default)
    {
        if (!TryGetEntity(entityId, out var entity))
            return false;

        if (entity.Properties == null)
        {
            entity.Properties = new();
            _entities[(int)entityId] = entity;
        }
        for (var index = 0; index < entity.Properties.Count; index++)
        {
            var entityProperty = entity.Properties[index];
            if (entityProperty.Type == property)
            {
                entityProperty.Value = value;
                entity.Properties[index] = entityProperty;
                return true;
            }
        }
        entity.Properties.Add(new Property(property, value));
        return true;
    }
    public bool RunEffect(Rule effect)
    {
        if (!effect.If.IsTrue(_ctx))
            return false;

        return effect.Then.MakeTrue(_ctx);
    }
}

struct PropertyValue : IEquatable<PropertyValue>
{
    public string? Value;
    public int IntValue;
    public static implicit operator PropertyValue(string s) => new PropertyValue
    {
        Value = s,
        IntValue = Int32.MinValue,
    };
    public static implicit operator PropertyValue(int i) => new PropertyValue
    {
        Value = null,
        IntValue = i,
    };
    public static implicit operator PropertyValue(bool b) => new PropertyValue
    {
        Value = null,
        IntValue = b ? 1 : 0,
    };
    public bool BoolValue => IntValue != 0;

    public bool Equals(PropertyValue other)
    {
        return Value == other.Value && IntValue == other.IntValue;
    }
    public override bool Equals(object? obj)
    {
        return obj is PropertyValue other && Equals(other);
    }
    public override int GetHashCode()
    {
        return HashCode.Combine(Value, IntValue);
    }
    public static bool operator ==(PropertyValue left, PropertyValue right)
    {
        return left.Equals(right);
    }
    public static bool operator !=(PropertyValue left, PropertyValue right)
    {
        return !left.Equals(right);
    }
}
struct Property
{
    public PropertyType Type;
    public PropertyValue Value;
    public Property(PropertyType type, PropertyValue value)
    {
        Type = type;
        Value = value;
    }
}

interface IPredicate
{
    bool IsTrue(PredicateContext ctx);
    bool MakeTrue(PredicateContext ctx);
}
class True : IPredicate
{
    public bool IsTrue(PredicateContext ctx) => true;
    public bool MakeTrue(PredicateContext ctx)
    {
        throw new NotImplementedException();
    }
}

class And : IPredicate
{
    private List<IPredicate> Predicates = new();
    public And(params IPredicate[] predicates)
    {
        Predicates.AddRange(predicates);
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return Predicates.All(p => p.IsTrue(ctx));
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        bool res = true;
        foreach (var predicate in Predicates)
        {
            res = res && predicate.MakeTrue(ctx);
        }
        return res;
    }
}

class PredicateContext
{
    public readonly Database Database;
    public long EntityId;

    public PredicateContext(Database database)
    {
        Database = database;
    }
}
class EntityExists : IPredicate
{
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.EntityExists(ctx.EntityId);
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        if (!ctx.Database.EntityExists(ctx.EntityId))
            ctx.EntityId = ctx.Database.AllocateEntity();
        return true;
    }
}

class PropertyEquals : IPredicate
{
    public readonly PropertyType Property;
    public readonly PropertyValue Value;

    public PropertyEquals(PropertyType property, PropertyValue value)
    {
        Property = property;
        Value = value;
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity) && entity.GetProperty(Property) == Value;
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        return ctx.Database.SetProperty(ctx.EntityId, Property, Value);
    }
}
class HasProperty : IPredicate
{
    public readonly PropertyType Property;

    public HasProperty(PropertyType property)
    {
        Property = property;
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity) && entity.TryGetProperty(Property, out _);
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        return ctx.Database.SetProperty(ctx.EntityId, Property);
    }
}


struct Entity
{
    public long Id;
    public List<Property>? Properties;
    public Entity(params Property[] properties) : this()
    {
        Properties ??= new();
        Properties.AddRange(properties);
    }
    public bool TryGetProperty(PropertyType property, out PropertyValue value)
    {
        if (Properties == null)
        {
            value = default;
            return false;
        }
        foreach (var p in Properties)
        {
            if (p.Type == property)
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
    public PropertyValue GetProperty(PropertyType property)
    {
        if (Properties != null)
            return Properties.FirstOrDefault(p => p.Type == property).Value;

        return default;
    }
}

struct Rule
{
    public string Name;
    public IPredicate If;
    public IPredicate Then;
    public Rule(string name, IPredicate @if, IPredicate then)
    {
        Name = name;
        If = @if;
        Then = then;
    }
}