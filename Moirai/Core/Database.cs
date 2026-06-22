using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    // Named weighted tables (index 0 is a reserved sentinel so id 0 means "no table").
    public readonly List<TableDefinition> Tables = new() { null! };

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

    /// <summary>When non-null, a step-through debugger observes execution through this hook (see <see cref="IDebugHook"/>). Null = no overhead.</summary>
    public IDebugHook? DebugHook;

    /// <summary>0-based source lines that carry an executable statement, collected by the parser. Used by the debugger to snap a breakpoint to the nearest runnable line.</summary>
    public readonly HashSet<int> DebugStatementLines = new();

    private ExecuteContext _ctx;

    private List<Entity> _entities = new() { default };
    public IEnumerable<Entity> Entities => _entities.Skip(1);

    // --- In-memory query backend (world state lives only here; pick/each scan it directly) ---

    /// <summary>Entity ids bucketed by type, in allocation (== id) order. Append-only (dead entities are
    /// flagged, never removed). Lets a pick/each visit only the candidate type instead of all entities.</summary>
    private List<EntityId>[] _perTypeEntities = System.Array.Empty<List<EntityId>>();

    // Lightweight index for the dominant query shape: for each indexed bool property, the set of entity ids
    // currently holding `true`, kept in ascending id order. Without it a pick/each over a type scans every
    // entity ever created (dead/false rows accumulate forever); with it, a `prop`/`prop = true` conjunct
    // routes straight to the live rows. Only the `true` side is tracked — a fresh entity defaults to false
    // and is simply absent, so the set is always exact without create-time seeding. The full predicate is
    // still re-checked per candidate, so the index only narrows what is visited, never the result.
    private readonly HashSet<PropertyId> _indexedBoolProps = new();
    private readonly Dictionary<PropertyId, SortedSet<uint>> _boolIndex = new();
    private static readonly SortedSet<uint> EmptyUintSet = new();

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
            new EntityType("Time", 1) { IsSingleton = true }.DeclareProperty("year", PropYear.Id,
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
        _ctx.Reseed(seed);
    }


    // Cached instance id per `singleton`-declared type, so #Type lookups are O(1) instead of an
    // entity scan. Cleared on Init(); kept correct by AllocateEntity. GetSingleton falls back to a
    // scan if the cache misses, so this is purely an optimization layer.
    private readonly Dictionary<uint, EntityId> _singletons = new();

    public bool TryGetSingleton(EntityTypeId type, out EntityId id) => _singletons.TryGetValue(type.Id, out id);

    public EntityId AllocateEntity(EntityTypeId entityType, string? name = null)
    {
        var type = GetEntityType(entityType);
        Entity e = new(type);

        if (!String.IsNullOrEmpty(name))
        {
            e.SetProperty(PropName, name);
        }

        e.Id = new EntityId((uint)_entities.Count);
        _entities.Add(e);
        if (type.IsSingleton)
            _singletons[entityType.Id] = e.Id;
        _perTypeEntities[(int)entityType.Id].Add(e.Id);
        CurrentChangeset.RecordCreate(e);
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
        // Reads are served entirely from the in-memory mirror; there is no need to touch SQLite here.
        // (This previously ran a SELECT whose result was discarded — pure overhead per call.)
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

        // Maintain the in-memory bool index: track only entities currently holding `true`.
        if (_indexedBoolProps.Contains(property))
        {
            if (!_boolIndex.TryGetValue(property, out var set))
                _boolIndex[property] = set = new SortedSet<uint>();
            if (value.BoolValue) set.Add(entityId.Id);
            else set.Remove(entityId.Id);
        }

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

    public bool IsCollectionProperty(PropertyId prop)
    {
        var t = GetEntityType(prop.TypeId);
        return prop.Id < t.Properties.Count && t.Properties[(int)prop.Id].IsCollection;
    }

    /// <summary>
    /// Stable per-property key stored in the <c>collection.prop</c> column, also emitted inline by the
    /// SQL form of contains/count. Packs (type, property index) so different types' props never collide.
    /// </summary>
    public static long CollPropKey(PropertyId p) => ((long)p.TypeId.Id << 32) | p.Id;

    // Multi-valued (collection) properties, keyed by (owner, packed type/prop). HashSet gives set
    // semantics (idempotent add, like the old `INSERT OR IGNORE`).
    private readonly Dictionary<(EntityId owner, long propKey), HashSet<EntityId>> _collections = new();

    public void AddToCollection(EntityId owner, PropertyId coll, EntityId value)
    {
        var key = (owner, CollPropKey(coll));
        if (!_collections.TryGetValue(key, out var set))
            _collections[key] = set = new HashSet<EntityId>();
        set.Add(value);
    }

    public void RemoveFromCollection(EntityId owner, PropertyId coll, EntityId value)
    {
        if (_collections.TryGetValue((owner, CollPropKey(coll)), out var set))
            set.Remove(value);
    }

    public bool CollectionContains(EntityId owner, PropertyId coll, EntityId value) =>
        _collections.TryGetValue((owner, CollPropKey(coll)), out var set) && set.Contains(value);

    public int CollectionCount(EntityId owner, PropertyId coll) =>
        _collections.TryGetValue((owner, CollPropKey(coll)), out var set) ? set.Count : 0;

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

    public bool GetTableDefinition(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TableDefinition? table)
    {
        for (int i = 1; i < Tables.Count; i++)
        {
            if (Tables[i].Name == name)
            {
                table = Tables[i];
                return true;
            }
        }

        table = null;
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

    // selfVarIndex >= 0 binds a `$self` value-stack slot for the action body (used by scheduled
    // `schedule(...)` sites). It is set INSIDE the body scope below so it is gone by the time
    // RunTriggers runs — otherwise the leftover $self pollutes the value stack and corrupts the
    // computed-vs-SQL-variable decision for $new in trigger pick/each predicates (e.g. `ruler = $new`).
    public bool RunAction(EventTrigger eventTrigger, int selfVarIndex = -1, EntityId self = default)
    {
        // Console.WriteLine($"[{action.Name}]");
        CurrentChangeset = new Changeset(History?.Changesets.Count ?? -1, eventTrigger.Name, _ctx.Year);
        _currentActionId = eventTrigger.Id;
        _currentAction = eventTrigger;
        // _ctx.Values.Clear();

        var prof = ExecProfiler;
        var scope = prof?.Begin() ?? default;
        bool success = true;

        // NOT a using statement
        // The event body draws from this event's own RNG stream (restored on exit), so its randomness
        // is independent of every other rule. Triggers fired below get their own streams in RunTriggers.
        using (_ctx.UseStream(eventTrigger.RngStreamId))
        using (var s = _ctx.RunScope(false))
        {
            if (selfVarIndex >= 0)
                _ctx.SetArgument(selfVarIndex, self);
            DebugHook?.OnEnterFrame(DebugFrameKind.Event, eventTrigger.Name, eventTrigger.DebugScopeRoot, _ctx.ValueOffset);
            for (var index = 0; index < eventTrigger.Effects.Count; index++)
            {
                var e = eventTrigger.Effects[index];
                if (e is CallInstruction { Value: AssignPick { VariableIndex: -1 } })
                    throw new NotImplementedException("Arg index -1 on p " + index);

                DebugHook?.OnStatement(e, _ctx);
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

            DebugHook?.OnExitFrame();
            if (success && CurrentChangeset.Changes.Count != 0)
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

    // Triggers grouped by the (entity type, when-type) they react to, so RunTriggers only evaluates
    // the triggers that can possibly match a given change instead of scanning every trigger for every
    // change. Built lazily (Triggers is fixed after parsing); preserves trigger declaration order
    // within each bucket so firing order is unchanged.
    private Dictionary<(EntityTypeId, EventTrigger.WhenType), List<EventTrigger>>? _triggerIndex;

    private List<EventTrigger>? TriggersFor(EntityTypeId type, EventTrigger.WhenType whenType)
    {
        if (_triggerIndex == null)
        {
            var index = new Dictionary<(EntityTypeId, EventTrigger.WhenType), List<EventTrigger>>();
            foreach (var t in Triggers)
            {
                var key = (t.When.Item2, t.When.Item1);
                if (!index.TryGetValue(key, out var list))
                    index[key] = list = new List<EventTrigger>();
                list.Add(t);
            }

            _triggerIndex = index;
        }

        return _triggerIndex.TryGetValue((type, whenType), out var result) ? result : null;
    }

    private void RunTriggers(Changeset cs)
    {
        var prof = ExecProfiler;
        foreach (Changeset.Changed changed in cs.Changes)
        {
            _ctx.PrevEntity = changed.Prev;

            // Only triggers registered for this change's entity type AND when-type can match; look
            // them up rather than scanning every trigger (a null-Prev change = create, else change).
            var whenType = changed.Prev.Id.IsNull
                ? EventTrigger.WhenType.Created
                : EventTrigger.WhenType.Changed;
            var triggers = TriggersFor(changed.New.Type, whenType);
            if (triggers == null)
                continue;

            foreach (var trigger in triggers)
            {
                // Property gating: a `when Changed` trigger only needs evaluation when a property its
                // predicate actually reads changed on this entity. The alive-gated death triggers would
                // otherwise be re-checked on every prosperity/devotion/age write. (when_created triggers
                // fire on creation and are not gated.)
                if (whenType == EventTrigger.WhenType.Changed)
                {
                    if (!trigger.GatingComputed)
                    {
                        trigger.GatingProps = ComputeGatingProps(trigger);
                        trigger.GatingComputed = true;
                    }

                    var gp = trigger.GatingProps;
                    if (gp != null)
                    {
                        bool relevant = false;
                        for (int gi = 0; gi < gp.Length; gi++)
                            if (changed.Prev.TryGetProperty(gp[gi], out _)) { relevant = true; break; }
                        if (!relevant)
                            continue;
                    }
                }

                EventAttemptCount++;
                var scope = prof?.Begin() ?? default;
                bool matched = false;
                // Each trigger evaluates and runs on its own RNG stream (independent of the event that
                // produced the changeset and of other triggers).
                using (_ctx.UseStream(trigger.RngStreamId))
                using (var s = _ctx.RunScope(false))
                {
                    // Entity type + when-type already matched via the index.
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
                        CurrentChangeset = new(CurrentChangeset.Id, trigger.Name, _ctx.Year);
                        DebugHook?.OnEnterFrame(DebugFrameKind.Trigger, trigger.Name, trigger.DebugScopeRoot, _ctx.ValueOffset);
                        foreach (var e in trigger.Effects)
                        {
                            DebugHook?.OnStatement(e, _ctx);
                            if (!e.Execute(_ctx).BoolValue)
                                break;
                        }
                        DebugHook?.OnExitFrame();
                        if (CurrentChangeset.Changes.Count != 0)
                            History?.AddChangeset(CurrentChangeset);
                    }
                }

                // "matched" means the predicate matched and the trigger's effects ran.
                prof?.RecordTrigger(trigger, scope, matched);
            }
        }

        _ctx.PrevEntity = default;
    }

    // The set of the trigger entity-type's own properties read by a `when Changed` predicate. Returns
    // null ("ungatable, always evaluate") when there is no predicate, the predicate reads none of the
    // entity's own properties, or it contains a construct we don't statically analyse — so gating can
    // only ever skip evaluations that could not have changed the predicate's value (never under-fire).
    private static PropertyId[]? ComputeGatingProps(EventTrigger trigger)
    {
        var predicate = trigger.When.Item3;
        if (predicate == null)
            return null;

        var acc = new HashSet<PropertyId>();
        if (!CollectReadProps(predicate, trigger.When.Item2, acc) || acc.Count == 0)
            return null;
        return acc.ToArray();
    }

    // Walk a predicate collecting PropertyIds it reads on `entityType` (the candidate). Returns false if
    // the predicate uses a node we can't fully analyse (function call, random, …) — caller treats that
    // as "always evaluate". Over-collecting is safe (just less optimal); under-collecting is not, hence
    // the conservative false on anything unrecognised.
    private static bool CollectReadProps(IValue? value, EntityTypeId entityType, HashSet<PropertyId> acc)
    {
        switch (value)
        {
            case null:
            case Literal:
                return true;
            case PropertyPath p:
                if (p.Segments != null)
                    foreach (var seg in p.Segments)
                    {
                        if (seg.Call != null)
                            return false; // method call in the path — not statically analysable
                        if (seg.Property.IsValid && seg.Property.TypeId == entityType)
                            acc.Add(seg.Property);
                    }

                return true;
            case BinaryOperator b:
                return CollectReadProps(b.Left, entityType, acc) && CollectReadProps(b.Right, entityType, acc);
            case And a:
                foreach (var pr in a.Predicates)
                    if (!CollectReadProps(pr, entityType, acc))
                        return false;
                return true;
            case MathUnary m:
                return CollectReadProps(m.Arg, entityType, acc);
            case IsOfType io:
                return CollectReadProps(io.Entity, entityType, acc);
            default:
                return false;
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
        Console.WriteLine(Path.GetFullPath("."));
        _singletons.Clear();
        _collections.Clear();
        _boolIndex.Clear();
        _indexedBoolProps.Clear();
        _perTypeEntities = new List<EntityId>[Types.Count];
        for (int i = 0; i < _perTypeEntities.Length; i++)
            _perTypeEntities[i] = new List<EntityId>();
        // Index the non-collection bool properties of user types (the dominant pick/each discriminants).
        foreach (var t in Types.Skip(1))
            foreach (var p in t.Properties.Skip(4))
                if (!p.IsCollection && p.Type.BaseType == PropertyValue.ValueBaseType.Bool)
                    _indexedBoolProps.Add(p.PropertyId);

        Profiler.Init(this);
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

    // Formerly backed up the in-memory SQLite DB to hello.db for inspection; world state now lives only in
    // the engine, so this is a no-op. Kept so the "Save" hub method and tests still call something.
    public void Commit()
    {
    }

    // pick T $v: (pred). varIdx is $v's value-stack slot — bound to each candidate before the predicate is
    // evaluated against it.
    // Reservoir sampling, size 1: one pass over the candidates, O(1) memory, uniform over the matches —
    // the k-th match replaces the current pick with probability 1/k. Only matches 2..n cost an RNG draw.
    public bool PickRandom(EntityTypeId entityTypeId, IValueSql? predicate, int varIdx, out EntityId id)
    {
        id = default;
        if (predicate == null && !entityTypeId.IsValid)
            return false;

        uint count = 0;
        foreach (var candidate in Candidates(entityTypeId, predicate, varIdx))
        {
            if (predicate != null)
            {
                _ctx.SetArgument(varIdx, candidate);
                if (!predicate.IsTrue(_ctx))
                    continue;
            }

            count++;
            if (count == 1 || _ctx.Rnd.GenerateNext(count) == 0)
                id = candidate;
        }

        return count > 0;
    }

    public bool FindAll(EntityTypeId entityTypeId, IValueSql? predicate, int varIdx, ref List<EntityId> results) =>
        FindAll(entityTypeId, predicate, varIdx, ref results, out _);

    // each T $v: (pred). See PickRandom for the varIdx contract.
    public bool FindAll(EntityTypeId entityTypeId, IValueSql? predicate, int varIdx, ref List<EntityId> results,
        out string? sql)
    {
        sql = null;
        results.Clear();
        if (predicate == null && !entityTypeId.IsValid)
            return false;

        foreach (var candidate in Candidates(entityTypeId, predicate, varIdx))
        {
            if (predicate != null)
            {
                _ctx.SetArgument(varIdx, candidate);
                if (!predicate.IsTrue(_ctx))
                    continue;
            }

            results.Add(candidate);
        }

        return true;
    }

    // Candidate stream for a scan. When the predicate constrains an indexed bool property of the query
    // variable to `true`, yields just that index bucket (skipping the accumulating false/dead rows);
    // otherwise yields every entity of the type. Both are in ascending id order, and the caller re-checks
    // the full predicate per candidate — so this only changes which rows are visited, never the result.
    private IEnumerable<EntityId> Candidates(EntityTypeId entityTypeId, IValueSql? predicate, int varIdx)
    {
        var indexed = TryGetBoolIndexCandidates(predicate, varIdx);
        if (indexed != null)
        {
            foreach (var raw in indexed)
                yield return new EntityId(raw);
            yield break;
        }

        foreach (var id in _perTypeEntities[(int)entityTypeId.Id])
            yield return id;
    }

    // Returns the id-ordered bucket for the first `<queryVar>.<indexedBool>` (= true) constraint found in
    // the predicate (an empty set if the prop is indexed but nothing is currently true), or null if the
    // predicate offers no usable indexed constraint (→ full type scan).
    private SortedSet<uint>? TryGetBoolIndexCandidates(IValueSql? predicate, int varIdx)
    {
        if (predicate != null && TryFindIndexedTrueProp(predicate, varIdx, out var prop))
            return _boolIndex.TryGetValue(prop, out var s) ? s : EmptyUintSet;
        return null;
    }

    // Walks conjunctions — both the dedicated And class and BinaryOperator.And, since either may appear —
    // for the first true-constrained indexed bool. Only descends AND nodes (an OR/NOT branch can't
    // guarantee the flag), so any bucket it returns is a superset of the full match set.
    private bool TryFindIndexedTrueProp(IValue node, int varIdx, out PropertyId prop)
    {
        switch (node)
        {
            case And and:
                foreach (var p in and.Predicates)
                    if (TryFindIndexedTrueProp(p, varIdx, out prop)) return true;
                prop = default;
                return false;
            case BinaryOperator { Op: BinaryOperator.Operator.And } andOp:
                return TryFindIndexedTrueProp(andOp.Left, varIdx, out prop)
                       || TryFindIndexedTrueProp(andOp.Right, varIdx, out prop);
            default:
                return TryMatchTrueBoolConjunct(node, varIdx, out prop);
        }
    }

    private bool TryMatchTrueBoolConjunct(IValue conjunct, int varIdx, out PropertyId prop)
    {
        // Bare bool path used as a predicate (`alive`) means `alive = true`.
        if (IsIndexedBoolPath(conjunct, varIdx, out prop))
            return true;
        if (conjunct is BinaryOperator { Op: BinaryOperator.Operator.Equals } bo)
        {
            if (IsIndexedBoolPath(bo.Left, varIdx, out prop) && IsTrueBoolLiteral(bo.Right))
                return true;
            if (IsIndexedBoolPath(bo.Right, varIdx, out prop) && IsTrueBoolLiteral(bo.Left))
                return true;
        }

        prop = default;
        return false;
    }

    private bool IsIndexedBoolPath(IValue v, int varIdx, out PropertyId prop)
    {
        prop = default;
        if (v is not PropertyPath pp) return false;
        if (pp.Mode != PropertyPath.PropertyPathMode.Variable || pp.VariableIndex != varIdx) return false;
        if (pp.Segments is not { Count: 1 } segs || segs[0].Call != null) return false;
        prop = segs[0].Property;
        return _indexedBoolProps.Contains(prop);
    }

    private static bool IsTrueBoolLiteral(IValue v) =>
        v is Literal lit && lit.Value.Type.BaseType == PropertyValue.ValueBaseType.Bool && lit.Value.BoolValue;

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
        // Entities referenced by this record (collected from the {$var} interpolation slots).
        // Lets the UI build per-entity biographies/filters without text-scanning the rendered string.
        public readonly EntityId[] Participants;
        // Tags of the event/trigger that emitted this record (from @tag(...)), for chronicle grouping.
        public readonly string[]? Tags;

        public Record(string text, long year, int changesetId, int actionId, EntityId[] participants, string[]? tags)
        {
            Text = text;
            Year = year;
            ChangesetId = changesetId;
            ActionId = actionId;
            Participants = participants;
            Tags = tags;
        }
    }

    public List<Record> Records = new();
    private int _currentActionId;
    // The event/trigger currently executing, captured so AppendRecord can stamp records with its tags.
    private EventTrigger? _currentAction;

    public void AppendRecord(string text, long year, IReadOnlyCollection<EntityId>? participants = null)
    {
        Records.Add(new(text, year, CurrentChangeset.Id, _currentActionId,
            participants?.ToArray() ?? Array.Empty<EntityId>(),
            _currentAction?.Tags?.ToArray()));
        DebugHook?.OnRecord(text, year);
    }

    internal Dictionary<(EntityId, int), long> _marked = new();

    public void Mark(EntityId eId, int eventIndex)
    {
        _marked[(eId, eventIndex)] = _ctx.Year;
    }

    public bool GetLastMarked(EntityId eId, int eventIndex, out long year)
    {
        return _marked.TryGetValue((eId, eventIndex), out year);
    }

    // --- Deferred per-entity effects (the `schedule(entity, year) { body }` DSL instruction) ---
    // Schedule sites (compiled bodies) are registered at parse time; the queue of pending firings is
    // in-memory runtime state, rebuilt from scratch on reset/hot-reload like the rest of the world.

    private readonly List<ScheduleSite> _scheduleSites = new();

    /// <summary>Number of registered schedule sites (used by the parser to assign each a unique id).</summary>
    public int ScheduleSiteCount => _scheduleSites.Count;

    /// <summary>Registers a compiled <c>schedule</c> body; returns its index, stored on the <see cref="ScheduleEffect"/>.</summary>
    public int RegisterScheduleSite(EventTrigger trigger, int selfVarIndex)
    {
        _scheduleSites.Add(new ScheduleSite(trigger, selfVarIndex));
        return _scheduleSites.Count - 1;
    }

    // (fireYear, boundEntity, siteIndex, seq). `seq` is a monotonic insertion counter giving a deterministic
    // tiebreak when several effects fall due the same year — all randomness flows through one Pcg32, so fire
    // order must be stable for runs to stay reproducible per seed.
    private readonly List<(long year, EntityId entity, int site, long seq)> _scheduled = new();
    private long _scheduleSeq;

    /// <summary>Enqueues a deferred body to fire when the simulation reaches <paramref name="year"/>.</summary>
    public void EnqueueScheduled(long year, EntityId entity, int site)
    {
        if (entity.IsNull)
            return;
        // Always fire strictly in the future so `schedule(x, #Time.year)` (or a body that re-schedules
        // itself) can never loop within a single year's drain.
        if (year <= _ctx.Year)
            year = _ctx.Year + 1;
        _scheduled.Add((year, entity, site, _scheduleSeq++));
    }

    /// <summary>
    /// Fires every scheduled effect whose year has arrived (<c>year &lt;= upToYear</c>, so a multi-year jump
    /// catches up), in deterministic (year, seq) order. Each firing runs through <see cref="RunAction"/>, so it
    /// opens its own changeset, lands in History under the site name, and replays triggers. Entities that no
    /// longer exist are skipped; the body itself guards state (e.g. <c>if $self.alive</c>).
    /// </summary>
    public void DrainScheduled(long upToYear)
    {
        if (_scheduled.Count == 0)
            return;

        List<(long year, EntityId entity, int site, long seq)>? due = null;
        var remaining = new List<(long, EntityId, int, long)>(_scheduled.Count);
        foreach (var s in _scheduled)
        {
            if (s.year <= upToYear)
                (due ??= new()).Add(s);
            else
                remaining.Add(s);
        }

        if (due == null)
            return;

        // Rebuild the queue with only the not-yet-due entries before firing, so bodies that enqueue further
        // (strictly future) effects append cleanly without disturbing this drain pass.
        _scheduled.Clear();
        _scheduled.AddRange(remaining);

        due.Sort((a, b) => a.year != b.year ? a.year.CompareTo(b.year) : a.seq.CompareTo(b.seq));

        foreach (var d in due)
        {
            if (!TryGetEntity(d.entity, out _))
                continue;
            var siteDef = _scheduleSites[d.site];
            // Bind $self inside RunAction's own body scope (not an enclosing one) so it is cleared
            // before RunAction replays triggers — see the note on RunAction's selfVarIndex parameter.
            RunAction(siteDef.Trigger, siteDef.SelfVarIndex, d.entity);
        }
    }


    public bool GetFunctionDefinition(string name, [NotNullWhen(true)] out FunctionDefinition? descriptor)
    {
        descriptor = Functions.FirstOrDefault(f => f.Name == name);
        return descriptor?.Name != null;
    }
}
