using System.Diagnostics;
using Pcg.Core;

public class Database
{
    private List<Entity> _entities = new() { default };
    internal List<Rule> Rules = new();
    public List<Action> Effects = new();

    public History? History;
    public Changeset CurrentChangeset;

    private PredicateContext _ctx;
    public Database()
    {
        _ctx = new PredicateContext(this);
    }
    public IEnumerable<Entity> Entities => _entities.Skip(1);

    public EntityId AllocateEntity(EntityType entityType)
    {
        Entity e = new();
        e.Properties = new() { new Property(PropertyType.Type, (int)entityType) };
        e.Id = new EntityId((long)_entities.Count);
        _entities.Add(e);
        CurrentChangeset.Changes?.Add(Change.Create(e.Id, entityType));

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
    public bool SetProperty(long entityId, PropertyType property, PropertyValue value = default)
    {
        if (!TryGetEntity(entityId, out var entity))
            return false;

        if (property == PropertyType.Id)
            throw new NotImplementedException();

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
        foreach (var a in this.Effects)
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
        if (History != null)
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

            if (!e.MakeTrue(_ctx))
            {
                if (CurrentChangeset.Changes.Count != 0)
                {
                    Console.Error.WriteLine("Action failed but left changes:");
                    History?.Changesets?.Add(CurrentChangeset);
                }
                return false;
            }
        }
        History?.Changesets?.Add(CurrentChangeset);
        return true;
        // if (_ctx.Query(effect.If, out var v))
        // {
        //     _ctx.EntityId = v.IntValue;
        //     return effect.Then.MakeTrue(_ctx);
        // }
        // return false;
    }
    private static ConsoleColor[] Colors = { ConsoleColor.Cyan, ConsoleColor.Magenta, ConsoleColor.Green, ConsoleColor.Yellow };
    public void PrintDb()
    {
        // Console.WriteLine("[DB]");
        bool any = false;
        foreach (var e in Entities)
        {
            any = true;
            string name = e.TryGetProperty(PropertyType.Name, out var nameprop) ? (nameprop.Value ?? "") : "";
            Console.ForegroundColor = Colors[e.GetProperty(PropertyType.Type).IntValue % Colors.Length];
            Console.WriteLine($"e{e.Id} {name}");
            Console.ResetColor();
            if (e.Properties != null)
                foreach (var property in e.Properties)
                {
                    if (property.Type != PropertyType.Name)
                        Console.WriteLine($"  {property.Type}: {StoryPrinter.Print(property.Value, property.Type)}");
                        // Console.WriteLine($"  {FormatProperty(property)}");
                }
        }
        if (!any)
            Console.WriteLine("<Empty>");
        Console.WriteLine();
    }
    private string FormatProperty(Property property)
    {
        switch (property.Type)
        {

            case PropertyType.Type:
                return "Type: " + (EntityType)property.Value.IntValue;
            case PropertyType.Alive:
                return property.Value.BoolValue ? "Alive" : "Dead";
            case PropertyType.Owner:
                return $"Owner: {property.Value.IntValue}";
            case PropertyType.Partner:
                return $"Partner: {property.Value.IntValue}";
            case PropertyType.Name:
                return $"Name: {property.Value.Value}";
            case PropertyType.Faction:
                return $"Faction: {(TryGetEntity(property.Value.IntValue, out var f) ? f.GetProperty(PropertyType.Name).Value : "")}";
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}