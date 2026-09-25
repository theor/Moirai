using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
﻿using System.Diagnostics.CodeAnalysis;
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

    private const uint TimeTypeId = 1;
    // The year as the built-in Time type actually declares it. PropertyId equality includes the type, so
    // PropYear itself (type 0) never matches a real write.
    internal static readonly PropertyId TimeYear = new(PropYear.Id, new EntityTypeId(TimeTypeId));

    public static Database Instance = null!;

    /// <summary>
    /// Where the engine's diagnostic output goes. Defaults to the console, which is right for the CLI,
    /// the test suite and the web server. A host without a console — the WebAssembly build — points this
    /// at its own sink instead, so the engine never has to know where it is running.
    /// </summary>
    public static Action<string> Log = Console.WriteLine;

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

    /// <summary>
    /// The year the world begins, read off the <c>Time</c> singleton at the end of <see cref="Init"/>.
    /// Stories rarely start at zero (w.sg starts at 764), so anything plotting or bucketing history has
    /// to know where the timeline actually starts rather than padding it from zero.
    /// </summary>
    public long StartYear { get; private set; }

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

    private ChunkedList<Entity> _entities = NewEntityList();
    private readonly Slab<Property> _slab = new();

    private static ChunkedList<Entity> NewEntityList()
    {
        var list = new ChunkedList<Entity>();
        list.Add(default); // id 0 is null
        return list;
    }
    public IEnumerable<Entity> Entities => _entities.Skip(1);

    // --- In-memory query backend (world state lives only here; pick/each scan it directly) ---

    /// <summary>Entity ids bucketed by type, in allocation (== id) order. Append-only (dead entities are
    /// flagged, never removed). Lets a pick/each visit only the candidate type instead of all entities.</summary>
    private IdSet[] _perTypeEntities = System.Array.Empty<IdSet>();

    // Lightweight index for the dominant query shape: for each indexed bool property, the set of entity ids
    // currently holding `true`, kept in ascending id order. Without it a pick/each over a type scans every
    // entity ever created (dead/false rows accumulate forever); with it, a `prop`/`prop = true` conjunct
    // routes straight to the live rows. Only the `true` side is tracked — a fresh entity defaults to false
    // and is simply absent, so the set is always exact without create-time seeding. The full predicate is
    // still re-checked per candidate, so the index only narrows what is visited, never the result.
    private readonly HashSet<PropertyId> _indexedBoolProps = new();
    private readonly Dictionary<PropertyId, IdSet> _boolIndex = new();

    // Equality index over reference and enum properties: (property, value) -> the ids holding it, in
    // ascending order. A pick or each whose predicate pins one of them -- `place = $c`, `owner = $new`,
    // `job = Job.Soldier` -- then visits only that bucket instead of the whole type. Keyed by IntValue,
    // because that is what `=` compares for a reference or an enum (PropertyValue.Equals: Value, null for
    // both, and IntValue). An unset property reads as IntValue 0, so a lookup for 0 cannot be answered
    // from the buckets and falls back to the scan.
    // Per indexed property, its buckets by value: a reference's value is an entity id and an enum's a small
    // index, both dense from 0, so a chunked array does what a dictionary keyed by (property, value) did
    // without ever rehashing. A value outside [0, MaxIndexedValue) is not indexed, and a lookup for one
    // says "cannot say" -- the scan, which is always right.
    private readonly Dictionary<PropertyId, ChunkedList<IdSet>> _eqIndex = new();
    private const int MaxIndexedValue = 1 << 22;

    private static bool Indexable(in PropertyValue v) => !v.HasText && v.IntValue >= 0 && v.IntValue < MaxIndexedValue;
    // Where every index bucket keeps its ids (see IdSet).
    private readonly Slab<uint> _idSlab = new();

    // Where a scan's own id lists live -- the single id of `$v = x`, the union of an `or` -- stacked, and
    // released when the scan ends. Growing it leaves an outer scan reading the old array, which is intact.
    private uint[] _scratch = new uint[64];
    private int _scratchTop;

    private Ids Scratch(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        if (_scratchTop + a.Length + b.Length > _scratch.Length)
        {
            var grown = new uint[Math.Max(_scratch.Length * 2, _scratchTop + a.Length + b.Length)];
            Array.Copy(_scratch, grown, _scratchTop);
            _scratch = grown;
        }

        // A sorted merge without duplicates: the union of two ascending id lists, still ascending.
        int start = _scratchTop, n = start, i = 0, j = 0;
        while (i < a.Length || j < b.Length)
        {
            uint next;
            if (j == b.Length || (i < a.Length && a[i] < b[j]))
                next = a[i++];
            else if (i == a.Length || b[j] < a[i])
                next = b[j++];
            else
            {
                next = a[i++];
                j++;
            }

            _scratch[n++] = next;
        }

        _scratchTop = n;
        return new Ids(_scratch, start, n - start);
    }

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
            new EntityType("Time", TimeTypeId) { IsSingleton = true }.DeclareProperty("year", PropYear.Id,
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

    /// <summary>
    /// The instance of a <c>singleton</c> type, created on the spot if the story has not made one yet. A
    /// singleton is one fact about the world, so writing to it (<c>set #Time.year = 764</c>) is enough to
    /// bring it into being; nobody should have to <c>create</c> the clock before they can set it.
    /// </summary>
    public EntityId EnsureSingleton(EntityTypeId type) =>
        TryGetSingleton(type, out var id) ? id : AllocateEntity(type, GetEntityType(type).Name.ToLowerInvariant());

    /// <summary>
    /// The clock, which a pass cannot run without. A story that never creates or sets Time gets one at
    /// year 0, in a changeset of its own so the history still says where it came from. Made on the first
    /// pass rather than in <see cref="Init"/>: until then nothing needs it, and a story that only runs its
    /// events directly (most unit tests) keeps exactly the entities it made.
    /// </summary>
    public EntityId EnsureTime()
    {
        var time = new EntityTypeId(TimeTypeId);
        if (TryGetSingleton(time, out var id))
            return id;
        CurrentChangeset = new Changeset(History?.Changesets.Count ?? -1, "time", _ctx.Year);
        id = EnsureSingleton(time);
        History?.AddChangeset(CurrentChangeset, _ctx.Year);
        return id;
    }

    public EntityId AllocateEntity(EntityTypeId entityType, string? name = null) =>
        AllocateEntity(entityType, name == null ? default : new PropertyValue(name));

    /// <summary>A new entity, named <paramref name="name"/> if that is non-empty text.</summary>
    public EntityId AllocateEntity(EntityTypeId entityType, PropertyValue name)
    {
        var type = GetEntityType(entityType);
        Entity e = new(type, _slab);

        if (name.HasText && name.IntValue > 0)
        {
            e.SetProperty(PropName, name);
        }

        e.Id = new EntityId((uint)_entities.Count);
        _entities.Add(e);
        if (type.IsSingleton)
            _singletons[entityType.Id] = e.Id;
        _perTypeEntities[(int)entityType.Id].Add(e.Id.Id, _idSlab);
        CurrentChangeset.RecordCreate(this, e.Id, entityType);
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

    /// <summary>
    /// Bumped on every property write. Lets a predicate remember something it derived from the world —
    /// <see cref="Related"/> keeps one side's ancestors across a whole pick — and know when to forget it.
    /// </summary>
    public long WriteVersion { get; private set; }

    public bool SetProperty(EntityId entityId, PropertyId property, PropertyValue value = default)
    {
        Profiler.Set(property);
        WriteVersion++;

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

        // Time.year and the clock are one fact. PassYears writes both; a story that sets the year itself
        // (the `create Time { year := 764 }` in an @start event) moves the clock at that moment, so the
        // records, marks and schedules that follow in the same event see the year the story chose rather
        // than 0.
        if (property == TimeYear)
            _ctx.Year = value.IntValue;

        if (_eqIndex.TryGetValue(property, out var buckets))
        {
            // The buckets are structs inside the chunked array: written through a ref to the slot.
            if (Indexable(prev) && prev.IntValue < buckets.Count)
                buckets.RefAt(prev.IntValue).Remove(entityId.Id);

            if (Indexable(value))
            {
                while (buckets.Count <= value.IntValue)
                    buckets.Add(default);
                buckets.RefAt(value.IntValue).Add(entityId.Id, _idSlab);
            }
        }

        // Maintain the in-memory bool index: track only entities currently holding `true`.
        if (_indexedBoolProps.Contains(property))
        {
            ref var set = ref CollectionsMarshal.GetValueRefOrAddDefault(_boolIndex, property, out _);
            if (value.BoolValue) set.Add(entityId.Id, _idSlab);
            else set.Remove(entityId.Id);
        }

        CurrentChangeset.RecordSet(this, entityId, entity.Type, property, prev);
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
        // A call() from inside another rule nests: the callee's firing records its caller as its cause,
        // and when it returns the caller gets back its attribution *and its changeset*. The callee opens a
        // changeset of its own, and without giving the caller's back, everything the caller did before
        // the call was never logged and no trigger ever saw it (w.sg's crown_monarch: the new king's
        // title, the realm's ruler). A top-level run gives it back too: left behind, the action's changeset
        // -- already closed and in the history -- caught the next year's Time.year write, so w.sg's history
        // said item_created and create_faction had changed the clock, holding the live Time entity (its year
        // drifting with the present) rather than a copy.
        var (savedId, savedAction, savedFiring) = (_currentActionId, _currentAction, _currentFiring);
        var savedChangeset = CurrentChangeset;
        _currentFiring = LogFiring(eventTrigger, savedFiring, default, false);
        _actionDepth++;
        try
        {
            return RunActionCore(eventTrigger, selfVarIndex, self);
        }
        finally
        {
            _actionDepth--;
            (_currentActionId, _currentAction, _currentFiring) = (savedId, savedAction, savedFiring);
            CurrentChangeset = savedChangeset;
        }
    }

    // How many RunActions are on the stack: above zero, a RunAction is a call() from inside a rule.
    private int _actionDepth;

    private bool RunActionCore(EventTrigger eventTrigger, int selfVarIndex, EntityId self)
    {
        // Console.WriteLine($"[{action.Name}]");
        CurrentChangeset = new Changeset(History?.Changesets.Count ?? -1, eventTrigger.Name, _ctx.Year)
            { Firing = _currentFiring, Buffer = AcquireBuffer() };
        var buffer = CurrentChangeset.Buffer;
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
                        Log("Action failed but left changes:");
                    }

                    success = false;
                    break;
                }
            }

            DebugHook?.OnExitFrame();
            if (success && CurrentChangeset.Changes.Count != 0)
                History?.AddChangeset(CurrentChangeset, _ctx.Year);
        }

        // Record the event's own effect time (excludes the triggers fired below).
        prof?.RecordEvent(eventTrigger, scope, success);
        eventTrigger.Attempts++;
        if (success)
            eventTrigger.Successes++;

        if (!success)
        {
            ReleaseBuffer(buffer);
            return false;
        }
        // _taggedEntities.Clear();
        // CurrentChangeset.GetTaggedEntities(_taggedEntities);

        // NEEDS to be run after the scope above is disposed
        // otherwise the value stack might still contain values from the event execution
        // which will then affect the sql generated, as it currently relies on the stack state to determine if a variable
        // needs to be computed or is part of a query
        // eg pick Item $2: ($2.owner = ...) owner might be computed instead of a sql var
        RunTriggers(CurrentChangeset);
        ReleaseBuffer(buffer);

        return true;
    }

    // Open changesets' buffers, reused: an event's goes back once its triggers have replayed it, a
    // trigger's once every trigger for that changeset has run. Nothing in the history points at one --
    // closing a changeset copies what it needs out -- so a returned buffer is free to be refilled.
    private readonly Stack<ChangeBuffer> _buffers = new();
    private ChangeBuffer? _timeBuffer;

    /// <summary>
    /// The scratch changeset a pass writes Time.year into between actions. Never logged -- the history
    /// catches the clock up from the entity itself -- so its buffer is kept and emptied, not remade.
    /// </summary>
    internal Changeset TimeChangeset()
    {
        (_timeBuffer ??= new ChangeBuffer(this)).Reset();
        return new Changeset(-1, "time", Int64.MaxValue) { Buffer = _timeBuffer };
    }
    private readonly List<ChangeBuffer> _triggerBuffers = new();

    private ChangeBuffer AcquireBuffer() => _buffers.TryPop(out var b) ? b : new ChangeBuffer(this);

    private void ReleaseBuffer(ChangeBuffer? buffer)
    {
        if (buffer == null)
            return;
        buffer.Reset();
        _buffers.Push(buffer);
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
        var buffer = cs.Buffer;
        if (buffer == null)
            return;
        // The triggers' own changesets, handed back when every trigger has run (see ReleaseBuffer). A
        // trigger that calls an event nests another RunTriggers, which stacks its buffers above these.
        int usedFrom = _triggerBuffers.Count;
        for (int ci = 0; ci < buffer.EntityCount; ci++)
        {
            var changed = buffer.Entities[ci];
            var prev = new PrevView(buffer, ci);
            _ctx.PrevEntity = prev;

            // Only triggers registered for this change's entity type AND when-type can match; look
            // them up rather than scanning every trigger (a created entity = create, else change).
            var whenType = changed.Created
                ? EventTrigger.WhenType.Created
                : EventTrigger.WhenType.Changed;
            var triggers = TriggersFor(changed.Type, whenType);
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
                        ComputeGating(trigger);

                    // Every property a `$old.p` conjunct needs must have been written...
                    var rp = trigger.RequiredProps;
                    if (rp != null)
                    {
                        bool all = true;
                        for (int ri = 0; ri < rp.Length; ri++)
                            if (!prev.Wrote(rp[ri])) { all = false; break; }
                        if (!all)
                            continue;
                    }

                    // ...and at least one property the predicate reads.
                    var gp = trigger.GatingProps;
                    if (gp != null)
                    {
                        bool relevant = false;
                        for (int gi = 0; gi < gp.Length; gi++)
                            if (prev.Wrote(gp[gi])) { relevant = true; break; }
                        if (!relevant)
                            continue;
                    }
                }

                EventAttemptCount++;
                trigger.Attempts++;
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
                    _ctx.SetArgument(varIdx, changed.Id);

                    if (trigger.When.Item3 == null || trigger.When.Item3.IsTrue(_ctx))
                    {
                        matched = true;
                        EventAttemptSuccess++;
                        trigger.Successes++;
                        // The trigger's own firing, caused by this change in the changeset it is replaying,
                        // and its records attributed to it (tags included) rather than to the event.
                        var (savedAction, savedFiring) = (_currentAction, _currentFiring);
                        _currentFiring = LogFiring(trigger, cs.Firing, changed.Id, changed.Created);
                        _currentAction = trigger;
                        CurrentChangeset = new(CurrentChangeset.Id, trigger.Name, _ctx.Year)
                            { Firing = _currentFiring, Buffer = AcquireBuffer() };
                        _triggerBuffers.Add(CurrentChangeset.Buffer!);
                        DebugHook?.OnEnterFrame(DebugFrameKind.Trigger, trigger.Name, trigger.DebugScopeRoot, _ctx.ValueOffset);
                        foreach (var e in trigger.Effects)
                        {
                            DebugHook?.OnStatement(e, _ctx);
                            if (!e.Execute(_ctx).BoolValue)
                                break;
                        }
                        DebugHook?.OnExitFrame();
                        if (CurrentChangeset.Changes.Count != 0)
                            History?.AddChangeset(CurrentChangeset, _ctx.Year);
                        (_currentAction, _currentFiring) = (savedAction, savedFiring);
                    }
                }

                // "matched" means the predicate matched and the trigger's effects ran.
                prof?.RecordTrigger(trigger, scope, matched);
            }
        }

        _ctx.PrevEntity = default;
        for (int i = usedFrom; i < _triggerBuffers.Count; i++)
            ReleaseBuffer(_triggerBuffers[i]);
        _triggerBuffers.RemoveRange(usedFrom, _triggerBuffers.Count - usedFrom);
    }

    /// <summary>
    /// The properties of a trigger's own entity type that its predicate reads, driving property-gated
    /// dispatch (see <see cref="RunTriggers"/>); null means "reacts to any change" (no predicate, or one
    /// we don't statically analyse). Computed lazily and cached. Exposed for tooling (the LSP CodeLens).
    /// </summary>
    public PropertyId[]? GetTriggerGatingProps(EventTrigger trigger)
    {
        if (!trigger.GatingComputed)
            ComputeGating(trigger);

        return trigger.GatingProps;
    }

    private static void ComputeGating(EventTrigger trigger)
    {
        trigger.GatingProps = ComputeGatingProps(trigger);
        // Only for a predicate the gate can read in full: one it cannot (a function call, a random draw)
        // is always evaluated, so that skipping it can never move its trigger's RNG stream.
        trigger.RequiredProps = trigger.GatingProps != null ? ComputeRequiredProps(trigger) : null;
        trigger.GatingComputed = true;
    }

    // The properties a `when Changed` predicate cannot be true without the change having written: those of
    // a top-level conjunct `$old.p` or `$old.p = <non-zero literal>`. $old of a property the change did not
    // write reads the default (false, 0, null), which fails either test, so skipping is exact.
    private static PropertyId[]? ComputeRequiredProps(EventTrigger trigger)
    {
        var acc = new List<PropertyId>();
        Visit(trigger.When.Item3);
        return acc.Count == 0 ? null : acc.Distinct().ToArray();

        void Visit(IValue? v)
        {
            switch (v)
            {
                case And and:
                    foreach (var p in and.Predicates)
                        Visit(p);
                    break;
                case BinaryOperator { Op: BinaryOperator.Operator.And } b:
                    Visit(b.Left);
                    Visit(b.Right);
                    break;
                case BinaryOperator { Op: BinaryOperator.Operator.Equals } eq:
                    if (OldProp(eq.Left) is { } l && eq.Right is Literal { Value.IntValue: not 0, Value.HasText: false })
                        acc.Add(l);
                    else if (OldProp(eq.Right) is { } r && eq.Left is Literal { Value.IntValue: not 0, Value.HasText: false })
                        acc.Add(r);
                    break;
                default:
                    if (OldProp(v) is { } bare)
                        acc.Add(bare);
                    break;
            }
        }

        // `$old.p`: the value-stack slot 0 RunTriggers binds $old to, and one property of the trigger's type.
        PropertyId? OldProp(IValue? v) =>
            v is PropertyPath { Mode: PropertyPath.PropertyPathMode.Variable, VariableIndex: 0, Segments: [{ Call: null } seg] }
            && seg.Property.IsValid && seg.Property.TypeId == trigger.When.Item2
                ? seg.Property
                : null;
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

        _entities = NewEntityList();
        foreach (var e in entities)
            _entities.Add(e);
    }

    public void Init()
    {
        _plans = new();
        _singletons.Clear();
        _collections.Clear();
        _boolIndex.Clear();
        _eqIndex.Clear();
        _indexedBoolProps.Clear();
        _perTypeEntities = new IdSet[Types.Count];
        for (int i = 0; i < _perTypeEntities.Length; i++)
            _perTypeEntities[i] = new IdSet();
        // Index the non-collection bool properties of user types (the dominant pick/each discriminants).
        foreach (var t in Types.Skip(1))
            foreach (var p in t.Properties.Skip(4))
                if (!p.IsCollection && p.Type.BaseType == PropertyValue.ValueBaseType.Bool)
                    _indexedBoolProps.Add(p.PropertyId);
                else if (!p.IsCollection && p.Type.BaseType is PropertyValue.ValueBaseType.Ref
                             or PropertyValue.ValueBaseType.Enum)
                    _eqIndex[p.PropertyId] = new ChunkedList<IdSet>();

        Profiler.Init(this);
        foreach (EventTrigger a in Actions)
        {
            if (a.Filter is FilterAtStart)
                RunAction(a);
        }

        // No catch-up from Time.year needed here: SetProperty already moved the clock when a @start event
        // set it.
        StartYear = _ctx.Year;
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
        int mark = _scratchTop;
        try
        {
        foreach (var raw in Candidates(entityTypeId, predicate, varIdx).Span)
        {
            var candidate = new EntityId(raw);
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
        }
        finally
        {
            _scratchTop = mark;
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

        int mark = _scratchTop;
        try
        {
            foreach (var raw in Candidates(entityTypeId, predicate, varIdx).Span)
            {
                var candidate = new EntityId(raw);
                if (predicate != null)
                {
                    _ctx.SetArgument(varIdx, candidate);
                    if (!predicate.IsTrue(_ctx))
                        continue;
                }

                results.Add(candidate);
            }
        }
        finally
        {
            _scratchTop = mark;
        }

        return true;
    }

    // count/sum/avg/min/max T $v: (pred, value). The scan is FindAll's -- same candidates, same predicate
    // check with $v bound -- folding each match's value instead of keeping a list, so nothing is allocated.
    // A value that runs a query of its own nests on the scratch stack above this scan's.
    public PropertyValue Aggregate(Aggregate a)
    {
        var predicate = a.Predicate;
        if (!a.EntityType.IsValid)
            return new PropertyValue(a.ResultType, 0);
        long count = 0;
        double acc = 0;
        int mark = _scratchTop;
        try
        {
            foreach (var raw in Candidates(a.EntityType, predicate, a.VariableIndex).Span)
            {
                var candidate = new EntityId(raw);
                _ctx.SetArgument(a.VariableIndex, candidate);
                if (predicate != null && !predicate.IsTrue(_ctx))
                    continue;

                count++;
                if (a.Kind == global::Aggregate.AggregateKind.Count)
                    continue;

                double v = a.Value!.Compute(_ctx).FloatValue;
                acc = a.Kind switch
                {
                    global::Aggregate.AggregateKind.Min => count == 1 ? v : Math.Min(acc, v),
                    global::Aggregate.AggregateKind.Max => count == 1 ? v : Math.Max(acc, v),
                    _ => acc + v,
                };
            }
        }
        finally
        {
            _scratchTop = mark;
        }

        if (a.Kind == global::Aggregate.AggregateKind.Count)
            return new PropertyValue(PropertyValue.TypeNumber, (int)count);
        if (a.Kind == global::Aggregate.AggregateKind.Avg && count > 0)
            acc /= count;
        return a.ResultType == PropertyValue.TypeNumber
            ? new PropertyValue(PropertyValue.TypeNumber, (int)Math.Round(acc))
            : new PropertyValue(a.ResultType, (float)acc);
    }

    // The ids a scan visits. When the predicate constrains an indexed bool property of the query variable
    // to `true`, just that index bucket (skipping the accumulating false/dead rows), or whatever Narrow
    // finds smaller; otherwise every entity of the type. All in ascending id order, and the caller re-checks
    // the full predicate per candidate — so this only changes which rows are visited, never the result.
    // Anything it builds lives in the scratch stack, which the caller releases.
    private Ids Candidates(EntityTypeId entityTypeId, IValueSql? predicate, int varIdx)
    {
        if (predicate == null)
            return _perTypeEntities[(int)entityTypeId.Id].Ids;

        var plan = PlanFor(predicate, varIdx);
        // The id-ordered bucket for the first `<queryVar>.<indexedBool>` (= true) constraint in the
        // predicate (empty if the prop is indexed but nothing is currently true).
        Ids? indexed = plan.BoolProp is { } prop
            ? _boolIndex.TryGetValue(prop, out var set) ? set.Ids : Ids.Empty
            : null;
        if (Narrow(plan.Narrow, entityTypeId) is { } narrowed
            && (indexed == null || narrowed.Count < indexed.Value.Count))
            indexed = narrowed;
        return indexed ?? _perTypeEntities[(int)entityTypeId.Id].Ids;
    }

    // What a predicate's shape says about narrowing its scan, worked out once per predicate: which bool
    // index it can use, and the tree of lookups Narrow evaluates. Only the values those lookups compare
    // against are computed per scan -- the resolving of paths and function arguments that used to be
    // redone, with a list per path and a dictionary per function call, on every pick.
    private sealed class QueryPlan(int varIdx, PropertyId? boolProp, NarrowPlan? narrow)
    {
        public readonly int VarIdx = varIdx;
        public readonly PropertyId? BoolProp = boolProp;
        public readonly NarrowPlan? Narrow = narrow;
    }

    // Keyed weakly: the Query page parses a fresh expression per query, and a plain dictionary would keep
    // every one of them alive for as long as the world.
    private System.Runtime.CompilerServices.ConditionalWeakTable<object, QueryPlan> _plans = new();

    private QueryPlan PlanFor(IValueSql predicate, int varIdx)
    {
        if (_plans.TryGetValue(predicate, out var plan) && plan.VarIdx == varIdx)
            return plan;
        PropertyId? boolProp = TryFindIndexedTrueProp(predicate, varIdx, out var prop) ? prop : null;
        plan = new QueryPlan(varIdx, boolProp, CompileNarrow(predicate, varIdx, null));
        _plans.AddOrUpdate(predicate, plan);
        return plan;
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

    // ---- narrowing a scan -----------------------------------------------------------------------------
    //
    // Narrow() returns a set of ids, ascending, guaranteed to contain every entity of the type for which the
    // predicate can be true -- or null when it cannot say. The caller still checks the full predicate on
    // each one, so a narrower set changes how many rows are visited and nothing else: the matches, their
    // order, and therefore the RNG draws a pick makes over them, are exactly what a full scan gives.
    //
    //   a and b        the smaller of what a and b allow (either alone is already a superset)
    //   a or b         the union, when both sides are known
    //   $v.p = x       the equality index's bucket for (p, x), for a reference or enum p
    //   $v = x         just x, if it is an entity of the scanned type
    //   f(args)        f's body, when it is one expression, read with its parameters bound to args --
    //                  which is how is_child_of($child, $new) becomes "the children of $new"
    //
    // where x is anything that does not depend on the scanned variable: a literal, a singleton, or a path
    // from another variable, evaluated once, up front. If it cannot be evaluated (a path through a null the
    // predicate would itself have short-circuited) that part is simply unknown.

    // A node of a compiled narrowing; null stands for "cannot say", everywhere.
    private abstract class NarrowPlan;

    // `and`: the smallest of what its parts allow, the first of equals (each alone is a superset).
    private sealed class AllPlan(NarrowPlan[] parts) : NarrowPlan
    {
        public readonly NarrowPlan[] Parts = parts;
    }

    // `or`: the union, when both sides are known.
    private sealed class AnyPlan(NarrowPlan left, NarrowPlan right) : NarrowPlan
    {
        public readonly NarrowPlan Left = left, Right = right;
    }

    // `=`: a lookup of one side's value, tried both ways round.
    private sealed class EqualsPlan(Resolved left, Resolved right) : NarrowPlan
    {
        public readonly Resolved Left = left, Right = right;
    }

    private NarrowPlan? CompileNarrow(IValue node, int varIdx, ArgScope? scope)
    {
        switch (node)
        {
            case And and:
                return All(and.Predicates.Select(p => CompileNarrow(p, varIdx, scope)));
            case BinaryOperator { Op: BinaryOperator.Operator.And } a:
                return All(new[] { CompileNarrow(a.Left, varIdx, scope), CompileNarrow(a.Right, varIdx, scope) });
            case BinaryOperator { Op: BinaryOperator.Operator.Or } o:
                return CompileNarrow(o.Left, varIdx, scope) is { } left && CompileNarrow(o.Right, varIdx, scope) is { } right
                    ? new AnyPlan(left, right)
                    : null;
            case BinaryOperator { Op: BinaryOperator.Operator.Equals } eq:
            {
                var l = Resolve(eq.Left, varIdx, scope);
                var r = Resolve(eq.Right, varIdx, scope);
                bool usable = (l.Kind == ResolvedKind.QueryVar && r.Kind == ResolvedKind.Independent)
                              || (r.Kind == ResolvedKind.QueryVar && l.Kind == ResolvedKind.Independent);
                return usable ? new EqualsPlan(l, r) : null;
            }
            case UserFunctionCall call
                when !call.Definition.IsInstanceMethod
                     && call.Definition.Instructions is [CallInstruction { Value: { } body }]
                     && call.Arguments.Length == call.Definition.Parameters.Length:
            {
                var args = new Dictionary<int, IValue>();
                for (int i = 0; i < call.Arguments.Length; i++)
                    args[call.Definition.Parameters[i].ParamIndex] = call.Arguments[i];
                return CompileNarrow(body, varIdx, new ArgScope(args, scope));
            }
            default:
                return null;
        }

        // A part that can never say anything is skipped at run time anyway, so it is dropped here.
        static NarrowPlan? All(IEnumerable<NarrowPlan?> parts)
        {
            var known = parts.OfType<NarrowPlan>().ToArray();
            return known.Length == 0 ? null : known.Length == 1 ? known[0] : new AllPlan(known);
        }
    }

    private Ids? Narrow(NarrowPlan? plan, EntityTypeId type)
    {
        switch (plan)
        {
            case AllPlan all:
            {
                Ids? best = null;
                foreach (var p in all.Parts)
                    if (Narrow(p, type) is { } b && (best == null || b.Count < best.Value.Count))
                        best = b;
                return best;
            }
            case AnyPlan any:
            {
                if (Narrow(any.Left, type) is not { } l) return null;
                if (Narrow(any.Right, type) is not { } r) return null;
                if (l.Count == 0) return r;
                if (r.Count == 0) return l;
                return Scratch(l.Span, r.Span);
            }
            case EqualsPlan eq:
                return NarrowEquals(eq.Left, eq.Right, type) ?? NarrowEquals(eq.Right, eq.Left, type);
            default:
                return null;
        }
    }

    private Ids? NarrowEquals(Resolved side, Resolved other, EntityTypeId type)
    {
        if (side.Kind != ResolvedKind.QueryVar || other.Kind != ResolvedKind.Independent)
            return null;
        if (!TryComputeIndependent(other, out var value) || value.HasText)
            return null;

        if (side.Props.Count == 0)
        {
            // The scanned entity itself: it can only be x.
            var only = value.Id.Id;
            return TryGetEntity(value.Id, out var e) && e.Type == type
                ? Scratch(new ReadOnlySpan<uint>(ref only), default)
                : Ids.Empty;
        }

        // An unset property reads as 0 too, and the buckets only hold values that were set.
        if (side.Props.Count != 1 || !_eqIndex.TryGetValue(side.Props[0], out var buckets) || value.IntValue == 0
            || !Indexable(value))
            return null;
        return value.IntValue < buckets.Count ? buckets[value.IntValue].Ids : Ids.Empty;
    }

    // A function body's parameters, bound to the calling expressions (which live in the scope outside).
    private sealed record ArgScope(Dictionary<int, IValue> Args, ArgScope? Outer);

    private enum ResolvedKind { Unknown, QueryVar, Independent }

    // What a value is, seen from the scan: the scanned variable followed by properties (QueryVar), or a
    // root the scan cannot change followed by properties (Independent) -- or something else (Unknown).
    private readonly record struct Resolved(ResolvedKind Kind, IValue? Root, List<PropertyId> Props);

    private static Resolved Unknown => new(ResolvedKind.Unknown, null, new List<PropertyId>());

    private Resolved Resolve(IValue v, int varIdx, ArgScope? scope)
    {
        switch (v)
        {
            case Literal:
                return new Resolved(ResolvedKind.Independent, v, new List<PropertyId>());
            case PropertyPath pp when pp.Segments == null || pp.Segments.All(seg => seg.Call == null):
            {
                var props = pp.Segments?.Select(seg => seg.Property).ToList() ?? new List<PropertyId>();
                if (pp.Mode == PropertyPath.PropertyPathMode.Singleton)
                    return scope == null && props.Count > 0
                        ? new Resolved(ResolvedKind.Independent, pp, new List<PropertyId>())
                        : Unknown;
                if (pp.Mode != PropertyPath.PropertyPathMode.Variable)
                    return Unknown;
                if (scope == null)
                    return pp.VariableIndex == varIdx
                        ? new Resolved(ResolvedKind.QueryVar, null, props)
                        : new Resolved(ResolvedKind.Independent, pp, new List<PropertyId>());

                // Inside a function: a parameter stands for the argument it was called with.
                if (!scope.Args.TryGetValue(pp.VariableIndex, out var arg))
                    return Unknown;
                var outer = Resolve(arg, varIdx, scope.Outer);
                if (outer.Kind == ResolvedKind.Unknown)
                    return outer;
                var chain = new List<PropertyId>(outer.Props);
                chain.AddRange(props);
                return outer with { Props = chain };
            }
            default:
                return Unknown;
        }
    }

    private bool TryComputeIndependent(Resolved r, out PropertyValue value)
    {
        value = default;
        try
        {
            value = r.Root!.Compute(_ctx);
        }
        catch (Exception)
        {
            return false;
        }

        foreach (var prop in r.Props)
        {
            if (value.HasText || value.IntValue == 0)
                return false;
            GetProperty(value.Id, prop, out value);
        }

        return true;
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
    
    /// <summary>
    /// One rule running: an event (scheduled, or called by another rule) or a trigger whose predicate
    /// matched. <see cref="Parent"/> is the firing it happened inside -- the event whose changeset a
    /// trigger reacted to, or the rule that call()ed an event -- so following parents from a record's
    /// firing answers "why did this happen". For a trigger, <see cref="Cause"/> is the entity whose
    /// change set it off, and <see cref="CauseCreated"/> says whether that change was its creation.
    /// Serials start at 1; 0 means "outside any rule".
    /// </summary>
    public readonly record struct Firing(int Serial, EventTrigger Rule, int Parent, long Year, EntityId Cause, bool CauseCreated);

    private readonly ChunkedList<Firing> _firings = new();

    public IReadOnlyList<Firing> Firings => _firings;

    public bool TryGetFiring(int serial, out Firing firing)
    {
        var ok = serial >= 1 && serial <= _firings.Count;
        firing = ok ? _firings[serial - 1] : default;
        return ok;
    }

    private int _currentFiring;

    private int LogFiring(EventTrigger rule, int parent, EntityId cause, bool created)
    {
        var serial = _firings.Count + 1;
        _firings.Add(new Firing(serial, rule, parent, _ctx.Year, cause, created));
        return serial;
    }

    public struct Record
    {
        /// <summary>The sentence. A record the engine wrote keeps its text in <see cref="RecordStore"/> and
        /// makes the string the first time it is read.</summary>
        // JsonInclude: the wire options ignore read-only properties, and without it the feed sends records
        // with no text at all (arrays survive that setting, which is why Participants alone would not show it).
        [JsonInclude, JsonPropertyOrder(-1)]
        public string Text => _store != null ? _store.Text(_index) : _text!;
        public readonly int ChangesetId;
        public readonly int ActionId;
        public readonly long Year;
        // Entities referenced by this record (collected from the {$var} interpolation slots).
        // Lets the UI build per-entity biographies/filters without text-scanning the rendered string.
        [JsonInclude]
        public EntityId[] Participants => _store != null ? _store.Participants(_index) : _participants!;

        /// <summary><see cref="Participants"/> without making an array: for a scan over every record.</summary>
        public ReadOnlySpan<EntityId> ParticipantSpan() =>
            _store != null ? _store.ParticipantSpan(_index) : _participants;
        // Tags of the event/trigger that emitted this record (from @tag(...)), for chronicle grouping.
        public readonly string[]? Tags;
        // How much this record matters to the story, from record('...', weight). Ordinary records weigh
        // DefaultWeight; 0 is background noise ("grew old") and anything above the default is a turning
        // point the chronicle can surface. Relative, not absolute: only the ordering means anything.
        public readonly int Weight;

        public const int DefaultWeight = 1;
        // The firing that wrote this record (see Database.Firing), 0 outside any rule. ActionId stays the
        // *event* the record belongs to -- event and trigger ids overlap, and the viewer groups and hides
        // records by event -- so the rule that actually wrote it is Rule, by name.
        public readonly int Firing;
        public readonly string? Rule;

        private readonly string? _text;
        private readonly EntityId[]? _participants;
        private readonly RecordStore? _store;
        private readonly int _index;

        public Record(string text, long year, int changesetId, int actionId, EntityId[] participants, string[]? tags,
            int weight = DefaultWeight, int firing = 0, string? rule = null)
        {
            Weight = weight;
            Firing = firing;
            Rule = rule;
            _text = text;
            Year = year;
            ChangesetId = changesetId;
            ActionId = actionId;
            _participants = participants;
            Tags = tags;
        }

        internal Record(RecordStore store, int index, long year, int changesetId, int actionId, string[]? tags,
            int weight, int firing, string? rule)
        {
            _store = store;
            _index = index;
            Weight = weight;
            Firing = firing;
            Rule = rule;
            Year = year;
            ChangesetId = changesetId;
            ActionId = actionId;
            Tags = tags;
        }
    }

    /// <summary>
    /// Where the records a pass writes keep their text and participants: stretches of shared slabs rather
    /// than a string and an array each, so writing a record allocates nothing. The string and the array are
    /// made when something reads them, once, and kept -- reading costs the reader, not the simulation.
    /// </summary>
    internal sealed class RecordStore
    {
        private readonly Slab<char> _chars = new();
        private readonly Slab<EntityId> _ids = new();
        private readonly ChunkedList<Body> _bodies = new();

        private struct Body
        {
            public char[] Chars;
            public int CharOffset, CharCount;
            public EntityId[] Ids;
            public int IdOffset, IdCount;
            public string? Text;
            public EntityId[]? Participants;
        }

        public int Add(System.Text.StringBuilder text, ReadOnlySpan<EntityId> participants)
        {
            var (chars, charOffset) = _chars.Take(text.Length);
            text.CopyTo(0, chars.AsSpan(charOffset, text.Length), text.Length);
            var (ids, idOffset) = _ids.Take(participants.Length);
            participants.CopyTo(ids.AsSpan(idOffset));
            _bodies.Add(new Body
            {
                Chars = chars, CharOffset = charOffset, CharCount = text.Length,
                Ids = ids, IdOffset = idOffset, IdCount = participants.Length,
            });
            return _bodies.Count - 1;
        }

        public string Text(int i)
        {
            ref var b = ref _bodies.RefAt(i);
            return b.Text ??= new string(b.Chars, b.CharOffset, b.CharCount);
        }

        public EntityId[] Participants(int i)
        {
            ref var b = ref _bodies.RefAt(i);
            return b.Participants ??= b.IdCount == 0 ? Array.Empty<EntityId>() : ParticipantSpan(i).ToArray();
        }

        public ReadOnlySpan<EntityId> ParticipantSpan(int i)
        {
            ref readonly var b = ref _bodies[i];
            return new ReadOnlySpan<EntityId>(b.Ids, b.IdOffset, b.IdCount);
        }
    }

    private readonly RecordStore _recordStore = new();

    // Where the strings a pass makes live -- a birth's name, a `var $x: '...'` -- when they are not a literal
    // or some value's existing string. Referenced by the PropertyValues that hold them, so the slab lives
    // exactly as long as the world does.
    private readonly Slab<char> _strings = new();

    /// <summary>The text in <paramref name="sb"/> as a string value held in this world's string slab.</summary>
    internal PropertyValue StringOf(System.Text.StringBuilder sb)
    {
        var (chars, offset) = _strings.Take(sb.Length);
        sb.CopyTo(0, chars.AsSpan(offset, sb.Length), sb.Length);
        return new PropertyValue(chars, offset, sb.Length);
    }

    public readonly ChunkedList<Record> Records = new();
    private int _currentActionId;
    // The event/trigger currently executing, captured so AppendRecord can stamp records with its tags.
    private EventTrigger? _currentAction;

    public void AppendRecord(string text, long year, IReadOnlyCollection<EntityId>? participants = null,
        int weight = Record.DefaultWeight)
    {
        Records.Add(new(text, year, CurrentChangeset.Id, _currentActionId,
            participants?.ToArray() ?? Array.Empty<EntityId>(),
            _currentAction?.TagArray, weight, _currentFiring, _currentAction?.Name));
        DebugHook?.OnRecord(text, year);
    }

    // What a record() statement calls: the text is still in the printer's builder, and goes from there
    // straight into the record store.
    internal void AppendRecord(System.Text.StringBuilder text, List<EntityId> participants, int weight)
    {
        var index = _recordStore.Add(text, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(participants));
        Records.Add(new(_recordStore, index, _ctx.Year, CurrentChangeset.Id, _currentActionId,
            _currentAction?.TagArray, weight, _currentFiring, _currentAction?.Name));
        DebugHook?.OnRecord(Records[Records.Count - 1].Text, _ctx.Year);
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

    // (boundEntity, siteIndex, firing), ordered by (fireYear, seq). `seq` is a monotonic insertion counter
    // giving a deterministic tiebreak when several effects fall due the same year — all randomness flows
    // through one Pcg32, so fire order must be stable for runs to stay reproducible per seed. `firing` is
    // the rule that scheduled it, which becomes the body's cause: a death scheduled at birth traces back to
    // the birth. A heap rather than a list: draining used to copy every pending entry into a new list each
    // year, which with a scheduled death per person was a year's worth of garbage for nothing.
    private readonly PriorityQueue<(EntityId entity, int site, int firing), (long year, long seq)> _scheduled = new();
    private List<(EntityId entity, int site, int firing)>? _due = new();
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
        _scheduled.Enqueue((entity, site, _currentFiring), (year, _scheduleSeq++));
    }

    /// <summary>
    /// Fires every scheduled effect whose year has arrived (<c>year &lt;= upToYear</c>, so a multi-year jump
    /// catches up), in deterministic (year, seq) order. Each firing runs through <see cref="RunAction"/>, so it
    /// opens its own changeset, lands in History under the site name, and replays triggers. Entities that no
    /// longer exist are skipped; the body itself guards state (e.g. <c>if $self.alive</c>).
    /// </summary>
    public void DrainScheduled(long upToYear)
    {
        // Take everything due before firing any of it, so bodies that enqueue further (strictly future)
        // effects cannot join this drain. A nested drain (a body that passes time) gets a list of its own.
        var due = _due ?? new List<(EntityId, int, int)>();
        _due = null;
        due.Clear();
        while (_scheduled.TryPeek(out _, out var key) && key.year <= upToYear)
            due.Add(_scheduled.Dequeue());

        foreach (var d in due)
        {
            if (!TryGetEntity(d.entity, out _))
                continue;
            var siteDef = _scheduleSites[d.site];
            // Bind $self inside RunAction's own body scope (not an enclosing one) so it is cleared
            // before RunAction replays triggers — see the note on RunAction's selfVarIndex parameter.
            // Run as if inside the rule that scheduled it, so that rule is the body's parent firing.
            var outer = _currentFiring;
            _currentFiring = d.firing;
            try
            {
                RunAction(siteDef.Trigger, siteDef.SelfVarIndex, d.entity);
            }
            finally
            {
                _currentFiring = outer;
            }
        }

        due.Clear();
        _due = due;
    }


    public bool GetFunctionDefinition(string name, [NotNullWhen(true)] out FunctionDefinition? descriptor)
    {
        descriptor = Functions.FirstOrDefault(f => f.Name == name);
        return descriptor?.Name != null;
    }
}
