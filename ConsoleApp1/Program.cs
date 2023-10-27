// See https://aka.ms/new-console-template for more information

using System.Collections;
using System.Diagnostics;

internal class Program
{
    public static void PrintDb(Database db)
    {
        Console.WriteLine("[DB]");
        bool any = false;
        foreach (var e in db.Entities)
        {
            any = true;
            Console.WriteLine($"e{e.Id}");
            if (e.Properties != null)
                foreach (var property in e.Properties)
                {
                    Console.WriteLine($"  {FormatProperty(property)}");
                }
        }
        if (!any)
            Console.WriteLine("<Empty>");
        Console.WriteLine();
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
            case PropertyType.Partner:
                return $"Partner: {property.Value.IntValue}";
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
                new Rule("Persons have liveliness", new PropertyEquals(PropertyType.Type, Properties.TypePerson),
                    new HasProperty(PropertyType.Alive)),
                new Rule("Items have owners", new PropertyEquals(PropertyType.Type, Properties.TypeItem),
                    new HasProperty(PropertyType.Owner)),
            },
            Effects =
            {
                new Action("Create person", 
                    new CreateEntity(), new SetProperty(PropertyType.Type, Properties.TypePerson),
                        new SetProperty(PropertyType.Alive, true)),
                new Action("Create item", 
                    new CreateEntity(), new SetProperty(PropertyType.Type, Properties.TypeItem),
                        new SetProperty(PropertyType.Owner, default)),
                new Action("Someone dies",
                    new And(new PropertyEquals(PropertyType.Type, Properties.TypePerson), new PropertyEquals(PropertyType.Alive, true)),
                    new SetProperty(PropertyType.Alive, false)),
                // new Action("Set item owner",
                //     new And(new PropertyEquals(PropertyType.Type, Properties.TypeItem), new PropertyEquals(PropertyType.Owner, default)),
                //     new SetProperty(PropertyType.Owner, new PredicateParameter(
                //         new And(new PropertyEquals(PropertyType.Type, Properties.TypePerson), new PropertyEquals(PropertyType.Alive, true))
                //     ))),
                new Action("Set item owner",
                    new PredicateParameter(
                        new And(new PropertyEquals(PropertyType.Type, Properties.TypePerson), new PropertyEquals(PropertyType.Alive, true))
                    ),
                    new PredicateParameter(
                        new And(new PropertyEquals(PropertyType.Type, Properties.TypeItem),
                            new And(new PropertyEquals(PropertyType.Owner, default), new PropertyNotEquals(PropertyType.Owner, PredicateParameter.Argument(0))))
                    ),
                    new SetProperty(1, PropertyType.Owner, PredicateParameter.Argument(0))),
                new Action("Two people marry",
                    new PredicateParameter(new And(
                        new PropertyEquals(PropertyType.Type, Properties.TypePerson),
                        new PropertyEquals(PropertyType.Alive, true),
                        new PropertyEquals(PropertyType.Partner, default))),
                    new PredicateParameter(new And(
                        new PropertyEquals(PropertyType.Type, Properties.TypePerson),
                        new PropertyNotEquals(PropertyType.Id, PredicateParameter.Argument(0)),
                        new PropertyEquals(PropertyType.Alive, true),
                        new PropertyEquals(PropertyType.Partner, default))),
                    new SetProperty(0, PropertyType.Partner, PredicateParameter.Argument(1)),
                    new SetProperty(1, PropertyType.Partner, PredicateParameter.Argument(0))
                ),
                // new Action("Set item owner2",
                //     
                //     new And(new PropertyEquals(PropertyType.Type, Properties.TypeItem), new PropertyEquals(PropertyType.Owner, default)),
                //     new SetProperty(PropertyType.Owner, new PredicateParameter(
                //         new And(
                //             new PropertyEquals(PropertyType.Type, Properties.TypePerson),
                //             new PropertyEquals(PropertyType.Alive, true),
                //             new PropertyEquals(PropertyType.Owner,  ))
                //     ))),
            },
        };
        db.RunAction("Create person");
        db.RunAction("Create person");
        PrintDb(db);
        db.RunAction("Two people marry");
        PrintDb(db);
        db.RunAction("Someone dies");
        PrintDb(db);
return;
        Console.WriteLine("  [ITEMS]");
        db.RunAction("Create item");
        db.RunAction("Create item");
        PrintDb(db);
        db.RunAction("Set item owner");
        // PrintDb(db);
        PrintDb(db);
        db.RunAction("Set item owner");
        PrintDb(db);


    }
}


internal enum PropertyType
{
    Id,
    Type,
    Alive,
    Owner,
    Partner,
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
    public List<Action> Effects = new();

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
    public bool SetProperty(PropertyValue entityId, PropertyType property, PropertyValue value = default)
    {
        return SetProperty(entityId.IntValue, property, value);
    }
    public bool SetProperty(long entityId, PropertyType property, PropertyValue value = default)
    {
        if (!TryGetEntity(entityId, out var entity))
            return false;

        if(property == PropertyType.Id)
            throw new System.NotImplementedException();
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
    public bool RunAction(string actionName)
    {
        foreach (var a in this.Effects)
        {
            if (a.Name == actionName)
            {
                return RunAction(a);
            }
        }
        return false;
    }
    public bool RunAction(Action effect)
    {
        _ctx.Values.Clear();
        int argCount = 0;
        for (var index = 0; index < effect.Effects.Count; index++)
        {
            var e = effect.Effects[index];
            if (e is PredicateParameter pp)
            {
                pp.ArgumentIndex = argCount++;
                effect.Effects[index] = pp;
                e = pp;
            }
            if (!e.MakeTrue(_ctx))
                return false;
        }
        return true;
        // if (_ctx.Query(effect.If, out var v))
        // {
        //     _ctx.EntityId = v.IntValue;
        //     return effect.Then.MakeTrue(_ctx);
        // }
        // return false;
    }
}

struct PropertyValue : IEquatable<PropertyValue>
{
    public string? Value;
    public long IntValue;
    public static implicit operator PropertyValue(string s) => new PropertyValue
    {
        Value = s,
        IntValue = Int32.MinValue,
    };
    public static implicit operator PropertyValue(long i) => new PropertyValue
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
}

interface IEffect
{
    bool MakeTrue(PredicateContext ctx);
}

class True : IPredicate
{
    public bool IsTrue(PredicateContext ctx) => true;
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
}

class Sequence : IEffect
{
    private List<IEffect> Predicates = new();
    public Sequence(params IEffect[] predicates)
    {
        Predicates.AddRange(predicates);
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
    public List<PropertyValue> Values = new();

    public PredicateContext(Database database)
    {
        Database = database;
    }
    public long EntityId => Values[^1].IntValue;

    public bool Query(IPredicate? predicate, out PropertyValue value)
    {
        if (predicate == null)
        {
            value = default;
            return true;
        }
        var iterationIdx = Values.Count;
        foreach (var entity in Database.Entities)
        {
            SetArgument(iterationIdx,entity.Id);
            if (predicate.IsTrue(this))
            {
                PopArgument();
                value = entity.Id;
                return true;
            }
        }
        PopArgument();
        value = default;
        return false;
    }
    public void PopArgument() => Values.RemoveAt(Values.Count - 1);
    public PropertyValue Argument(int idx)
    {
        return Values[idx];
    }
    public void SetArgument(int argumentIndex, PropertyValue value)
    {
        while(Values.Count <= argumentIndex)
            Values.Add(default);
        Values[argumentIndex] = value;
    }
    public void PushArgument(long entity)
    {
        Values.Add(entity);
    }
}

class EntityExists : IPredicate
{
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.EntityExists(ctx.EntityId);
    }
}

class CreateEntity : IEffect
{
    public bool MakeTrue(PredicateContext ctx)
    {
        // if (!ctx.Database.EntityExists(ctx.EntityId))
        ctx.PushArgument(ctx.Database.AllocateEntity());
        return true;
    }
}

class PropertyEquals : IPredicate
{
    public readonly PropertyType Property;
    public readonly PredicateParameter Value;

    public PropertyEquals(PropertyType property, PropertyValue value)
    {
        Property = property;
        Value = value;
    }
    public PropertyEquals(PropertyType property, PredicateParameter value)
    {
        Property = property;
        Value = value;
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity) && entity.GetProperty(Property) == Value.GetValue(ctx);
    }
}

class PropertyNotEquals : IPredicate
{
    public readonly PropertyType Property;
    public readonly PredicateParameter Value;

    public PropertyNotEquals(PropertyType property, PredicateParameter value)
    {
        Property = property;
        Value = value;
    }
    public bool IsTrue(PredicateContext ctx)
    {
        return ctx.Database.TryGetEntity(ctx.EntityId, out Entity entity) && entity.GetProperty(Property) != Value.GetValue(ctx);
    }
}

struct PredicateParameter : IEffect
{
    enum PredicateParameterType
    {
        Value,
        Predicate,
        Argument,
    }

    private readonly PredicateParameterType Type;
    private readonly IPredicate? Predicate;
    private readonly PropertyValue Value;
    public int ArgumentIndex;
    public PredicateParameter(IPredicate predicate) : this()
    {
        Predicate = predicate;
        Type = PredicateParameterType.Predicate;
    }
    public PredicateParameter(PropertyValue value) : this()
    {
        Value = value;
        Type = PredicateParameterType.Value;
    }
 
    private PredicateParameter(int argumentIdx) : this()
    {
        ArgumentIndex = argumentIdx;
        Type = PredicateParameterType.Argument;
    }
 
    public static PredicateParameter Argument(int idx) => new PredicateParameter(idx);
    public static implicit operator PredicateParameter(PropertyValue v) => new PredicateParameter(v);
    public readonly PropertyValue GetValue(PredicateContext ctx)
    {
        switch (Type)
        {
    
            case PredicateParameterType.Value:
                return Value;
            case PredicateParameterType.Predicate:
                return ctx.Query(Predicate, out var val) ? val : default;
            case PredicateParameterType.Argument:
                return ctx.Argument(ArgumentIndex);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        ctx.SetArgument(ArgumentIndex, ctx.Query(Predicate, out var val) ? val : default);
        return true;
    }
}

class SetProperty : IEffect
{
    public readonly PropertyType Property;
    public readonly int Target;
    public readonly PredicateParameter Parameter;

    public SetProperty(PropertyType property, PredicateParameter parameter)
    {
        Property = property;
        Parameter = parameter;
        Target = 0;
    }
    public SetProperty(PropertyType property, PropertyValue parameter)
    {
        Property = property;
        Parameter = parameter;
        Target = 0;
    }
    public SetProperty(int target, PropertyType property, PredicateParameter parameter)
    {
        Property = property;
        Parameter = parameter;
        Target = target;
    }
    public SetProperty(int target, PropertyType property, PropertyValue parameter)
    {
        Property = property;
        Parameter = parameter;
        Target = target;
    }
    public bool MakeTrue(PredicateContext ctx)
    {
        return ctx.Database.SetProperty(ctx.Argument(Target), Property, Parameter.GetValue(ctx));
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
        if (property == PropertyType.Id)
        {
            value = Id;
            return true;
        }
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
        if (property == PropertyType.Id)
            return Id;
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

struct Action
{
    public string Name;
    public List<IEffect> Effects;
    public Action(string name, IPredicate? @if, IEffect then)
    {
        Name = name;
        Effects = new()
        {
            new PredicateParameter(@if),
            then,
        };
    }
    public Action(string name, params IEffect[] effects)
    {
        Name = name;
        Effects = effects.ToList();
    }
}