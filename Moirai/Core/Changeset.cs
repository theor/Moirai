namespace Moirai.Core;

/// <summary>
/// The changeset log. A closed changeset is not a copy of every entity it touched -- that was a full
/// property array per entity per changeset, kept forever, and it was most of the heap: w.sg's
/// deepen_faith writes one property on every believer and paid two whole entities for each. It is a
/// short record per entity instead: the properties the changeset wrote (with their value before and
/// after), plus any other property that moved since the log last saw that entity.
///
/// <para>The second part is what keeps an entity's full state recoverable. Not every write reaches a
/// logged changeset -- the clock's yearly tick, an action that fails after writing, the name an entity
/// is created with -- so replaying only what changesets wrote would drift. The log keeps a shadow of
/// each entity as it last recorded it, and closing a changeset records every difference from it. Walking
/// an entity's records back from any changeset therefore rebuilds it exactly as it stood when that
/// changeset closed, which is what <see cref="Changeset.Changed.New"/> does, on demand.</para>
/// </summary>
public class History
{
    public readonly HistoryMode Mode;
    private readonly ChunkedList<Changeset> _changesets = new();
    public IReadOnlyList<Changeset> Changesets => _changesets;

    public History(HistoryMode mode = HistoryMode.Default)
    {
        Mode = mode;
    }

    [Flags]
    public enum HistoryMode
    {
        Default = 0,

        Story = 1,
    }

    internal readonly ChunkedList<EntityChange> Entities = new();
    internal readonly ChunkedList<PropRecord> Props = new();
    internal Database? Db;

    // Per entity id: the index + 1 of its latest record in Entities (0 = none), and its properties as the
    // log last recorded them.
    private int[] _last = Array.Empty<int>();
    private Shadow[] _shadow = Array.Empty<Shadow>();
    private readonly Slab<Property> _shadowSlab = new();

    // An entity's properties as the log last recorded them: a stretch of a shared slab, not an array each.
    private struct Shadow
    {
        public Property[]? Array;
        public int Offset;
        public int Count;
        public readonly Span<Property> Span => Array == null ? default : new(Array, Offset, Count);
    }

    internal struct EntityChange
    {
        public EntityId Id;
        public EntityTypeId Type;
        public bool Created;
        public int PropStart;
        public int PropCount;
        // Index + 1 of this entity's previous record, 0 for its first.
        public int Previous;
    }

    internal struct PropRecord
    {
        public PropertyId Prop;
        public PropertyValue Prev;
        public PropertyValue Next;
        // Written by the changeset (so Prev means something), as opposed to caught up from the shadow.
        public bool Touched;
    }

    /// <summary>
    /// Close a changeset and add it to the log, stamped with the year it closed in. Outside Init an action
    /// never spans two years, so that is the year it opened in too; inside Init the @start event that
    /// creates Time opens at year 0 and closes at the year the story set.
    /// </summary>
    public void AddChangeset(Changeset currentChangeset, long year)
    {
        currentChangeset.Year = year;
        if (currentChangeset.Buffer is { } buffer)
            Close(ref currentChangeset, buffer);
        _changesets.Add(currentChangeset);
    }

    private void Close(ref Changeset cs, ChangeBuffer buffer)
    {
        var db = Db = buffer.Db;
        int start = Entities.Count;
        for (int i = 0; i < buffer.EntityCount; i++)
        {
            ref var open = ref buffer.Entities[i];
            var id = open.Id.Id;
            db.TryGetEntity(open.Id, out var live);
            var props = live.RawProperties;

            int propStart = Props.Count;
            for (int d = open.FirstDelta; d >= 0; d = buffer.Deltas[d].Next)
            {
                ref var delta = ref buffer.Deltas[d];
                Props.Add(new PropRecord
                {
                    Prop = delta.Prop, Prev = delta.Prev, Next = props[(int)delta.Prop.Id].Value, Touched = true,
                });
            }

            EnsureCapacity(id);
            ref var shadowSlot = ref _shadow[id];
            bool hasShadow = shadowSlot.Array != null && shadowSlot.Count == props.Length;
            var shadow = shadowSlot.Span;
            for (int slot = 0; slot < props.Length; slot++)
            {
                if (!props[slot].Id.IsValid || (hasShadow && Same(shadow[slot], props[slot])))
                    continue;
                if (buffer.Wrote(i, (uint)slot))
                    continue;
                Props.Add(new PropRecord { Prop = props[slot].Id, Next = props[slot].Value });
            }

            if (!hasShadow)
            {
                (shadowSlot.Array, shadowSlot.Offset) = _shadowSlab.Take(props.Length);
                shadowSlot.Count = props.Length;
            }

            props.CopyTo(shadowSlot.Span);

            Entities.Add(new EntityChange
            {
                Id = open.Id, Type = open.Type, Created = open.Created,
                PropStart = propStart, PropCount = Props.Count - propStart, Previous = _last[id],
            });
            _last[id] = Entities.Count;
        }

        cs.Buffer = null;
        cs.Closed = this;
        cs.EntityStart = start;
        cs.EntityCount = buffer.EntityCount;
    }

    private void EnsureCapacity(uint id)
    {
        if (id < _last.Length)
            return;
        int size = Math.Max((int)id + 1, _last.Length * 2);
        Array.Resize(ref _last, size);
        Array.Resize(ref _shadow, size);
    }

    private static bool Same(in Property a, in Property b) =>
        a.Id == b.Id
        && a.Value.Type == b.Value.Type
        && a.Value.IntValue == b.Value.IntValue
        && BitConverter.SingleToInt32Bits(a.Value.FloatValue) == BitConverter.SingleToInt32Bits(b.Value.FloatValue)
        && string.Equals(a.Value.Value, b.Value.Value, StringComparison.Ordinal);

    /// <summary>The entity as it stood when the changeset holding record <paramref name="index"/> closed.</summary>
    internal Entity Materialize(int index)
    {
        ref readonly var change = ref Entities[index];
        var e = new Entity(Db!.GetEntityType(change.Type)) { Id = change.Id };
        var props = e.RawProperties;
        // Newest first, so the first value found for a slot is the one it held at close.
        for (int i = index + 1; i != 0; i = Entities[i - 1].Previous)
        {
            ref readonly var c = ref Entities[i - 1];
            for (int p = c.PropStart; p < c.PropStart + c.PropCount; p++)
            {
                ref readonly var rec = ref Props[p];
                if (!props[(int)rec.Prop.Id].Id.IsValid)
                    e.SetProperty(rec.Prop, rec.Next);
            }
        }

        return e;
    }

    internal Entity MaterializePrev(int index)
    {
        ref readonly var change = ref Entities[index];
        if (change.Created)
            return default;
        var e = new Entity(Db!.GetEntityType(change.Type)) { Id = change.Id };
        for (int p = change.PropStart; p < change.PropStart + change.PropCount; p++)
            if (Props[p].Touched)
                e.SetProperty(Props[p].Prop, Props[p].Prev);
        return e;
    }
}

/// <summary>
/// An append-only list in fixed-size chunks. It never copies what it holds -- a <see cref="List{T}"/> that
/// doubles copies everything it has into a new array, garbage the moment it is done -- and no chunk is
/// big enough for the large object heap. Growing costs one chunk every <c>1024</c> items and nothing in
/// between, which is what lets a simulated year allocate nothing.
/// </summary>
public sealed class ChunkedList<T> : IReadOnlyList<T>
{
    private const int Bits = 10, Size = 1 << Bits, Mask = Size - 1;
    private readonly List<T[]> _chunks = new();
    public int Count { get; private set; }

    public void Add(in T item)
    {
        if (Count >> Bits == _chunks.Count)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            _chunks.Add(new T[Size]);
            StorageStats.ChunkBytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        _chunks[Count >> Bits][Count & Mask] = item;
        Count++;
    }

    public ref readonly T this[int i]
    {
        get
        {
            if ((uint)i >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(i));
            return ref _chunks[i >> Bits][i & Mask];
        }
    }

    T IReadOnlyList<T>.this[int i] => this[i];

    internal ref T RefAt(int i) => ref _chunks[i >> Bits][i & Mask];

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(ChunkedList<T> list) : IEnumerator<T>
    {
        private int _i = -1;
        public T Current => list[_i];
        object? System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++_i < list.Count;
        public void Reset() => _i = -1;
        public void Dispose() { }
    }
}

/// <summary>
/// What an open changeset has touched: its entities in the order they were first touched, and for each one
/// the value every property it wrote held before -- before the *last* write to it, as the entity-copy
/// version recorded it. Owned and pooled by <see cref="Database"/>: an event's buffer goes back once its
/// triggers have run, so a pass reuses a handful of these instead of allocating per changeset.
/// </summary>
internal sealed class ChangeBuffer
{
    public readonly Database Db;
    public OpenEntity[] Entities = new OpenEntity[8];
    public int EntityCount;
    public OpenDelta[] Deltas = new OpenDelta[8];
    public int DeltaCount;
    private readonly Dictionary<uint, int> _indexOf = new();

    public ChangeBuffer(Database db) => Db = db;

    public struct OpenEntity
    {
        public EntityId Id;
        public EntityTypeId Type;
        public bool Created;
        public int FirstDelta;
        public int LastDelta;
    }

    public struct OpenDelta
    {
        public PropertyId Prop;
        public PropertyValue Prev;
        public int Next;
    }

    public void Reset()
    {
        Array.Clear(Deltas, 0, DeltaCount);
        EntityCount = 0;
        DeltaCount = 0;
        _indexOf.Clear();
    }

    public void RecordCreate(EntityId id, EntityTypeId type)
    {
        // First entry wins: a later write to a created entity lands on its creation.
        _indexOf.TryAdd(id.Id, EntityCount);
        Add(id, type, true);
    }

    public void RecordSet(EntityId id, EntityTypeId type, PropertyId property, PropertyValue prev)
    {
        if (!_indexOf.TryGetValue(id.Id, out var i))
        {
            _indexOf[id.Id] = i = EntityCount;
            Add(id, type, false);
        }
        else if (Entities[i].Created)
            return; // no point in recording a newly created entity's previous property value

        ref var entity = ref Entities[i];
        for (int d = entity.FirstDelta; d >= 0; d = Deltas[d].Next)
            if (Deltas[d].Prop.Id == property.Id)
            {
                Deltas[d].Prop = property;
                Deltas[d].Prev = prev;
                return;
            }

        if (DeltaCount == Deltas.Length)
            Array.Resize(ref Deltas, DeltaCount * 2);
        Deltas[DeltaCount] = new OpenDelta { Prop = property, Prev = prev, Next = -1 };
        if (entity.LastDelta < 0)
            entity.FirstDelta = DeltaCount;
        else
            Deltas[entity.LastDelta].Next = DeltaCount;
        entity.LastDelta = DeltaCount;
        DeltaCount++;
    }

    private void Add(EntityId id, EntityTypeId type, bool created)
    {
        if (EntityCount == Entities.Length)
            Array.Resize(ref Entities, EntityCount * 2);
        Entities[EntityCount++] = new OpenEntity
            { Id = id, Type = type, Created = created, FirstDelta = -1, LastDelta = -1 };
    }

    /// <summary>Whether entity <paramref name="i"/>'s property in <paramref name="slot"/> was written.</summary>
    public bool Wrote(int i, uint slot)
    {
        for (int d = Entities[i].FirstDelta; d >= 0; d = Deltas[d].Next)
            if (Deltas[d].Prop.Id == slot)
                return true;
        return false;
    }

    /// <summary>The value the property in <paramref name="slot"/> held before it was written, if it was.</summary>
    public bool TryGetPrev(int i, uint slot, out PropertyValue value)
    {
        for (int d = Entities[i].FirstDelta; d >= 0; d = Deltas[d].Next)
            if (Deltas[d].Prop.Id == slot)
            {
                value = Deltas[d].Prev;
                return true;
            }

        value = default;
        return false;
    }

    public Entity MaterializePrev(int i)
    {
        ref var open = ref Entities[i];
        if (open.Created)
            return default;
        var e = new Entity(Db.GetEntityType(open.Type)) { Id = open.Id };
        for (int d = open.FirstDelta; d >= 0; d = Deltas[d].Next)
            e.SetProperty(Deltas[d].Prop, Deltas[d].Prev);
        return e;
    }
}

/// <summary>
/// The previous state of the entity a trigger is replaying a change of, as <c>$old</c> reads it: the
/// value each property the changeset wrote held before, the default for anything it did not write, and
/// nothing at all for an entity the changeset created.
/// </summary>
public readonly struct PrevView
{
    private readonly ChangeBuffer? _buffer;
    private readonly int _index;

    internal PrevView(ChangeBuffer buffer, int index)
    {
        _buffer = buffer;
        _index = index;
    }

    public EntityId Id => _buffer == null || _buffer.Entities[_index].Created
        ? default
        : _buffer.Entities[_index].Id;

    public PropertyValue GetProperty(PropertyId property)
    {
        if (property == Database.PropId)
            return Id;
        ref var open = ref _buffer!.Entities[_index];
        if (property == Database.PropType)
            return open.Type;
        return _buffer.TryGetPrev(_index, property.Id, out var v) ? v : default;
    }

    /// <summary>Whether the change wrote this property (the test property-gated triggers make).</summary>
    public bool Wrote(PropertyId property) => _buffer != null && _buffer.Wrote(_index, property.Id);
}

public struct Changeset(int id, string actionName, long year)
{
    /// <summary>
    /// One entity a changeset touched. <see cref="Id"/>, <see cref="Type"/> and <see cref="Created"/> are
    /// free; <see cref="Prev"/> and <see cref="New"/> build an entity each time they are read, so a scan
    /// over the log should filter on the id first.
    /// </summary>
    public readonly struct Changed
    {
        private readonly History? _history;
        private readonly ChangeBuffer? _buffer;
        private readonly int _index;

        internal Changed(History history, int index)
        {
            _history = history;
            _buffer = null;
            _index = index;
        }

        internal Changed(ChangeBuffer buffer, int index)
        {
            _history = null;
            _buffer = buffer;
            _index = index;
        }

        public EntityId Id => _history != null ? _history.Entities[_index].Id : _buffer!.Entities[_index].Id;
        public EntityTypeId Type => _history != null ? _history.Entities[_index].Type : _buffer!.Entities[_index].Type;
        public bool Created => _history != null ? _history.Entities[_index].Created : _buffer!.Entities[_index].Created;

        /// <summary>The properties the changeset wrote, with the value each held before; default if it created the entity.</summary>
        public Entity Prev => _history != null ? _history.MaterializePrev(_index) : _buffer!.MaterializePrev(_index);

        /// <summary>The entity as the changeset left it (while it is still open: the live entity).</summary>
        public Entity New
        {
            get
            {
                if (_history != null)
                    return _history.Materialize(_index);
                _buffer!.Db.TryGetEntity(Id, out var live);
                return live;
            }
        }

        /// <summary>
        /// A closed change's value for <paramref name="property"/> when it closed, if that value moved in
        /// this changeset (or since the log last saw the entity). False means it is what it was the last
        /// time the entity appeared in the log -- or unset, if this is its first appearance.
        /// </summary>
        public bool TryGetNext(PropertyId property, out PropertyValue value)
        {
            ref readonly var c = ref _history!.Entities[_index];
            for (int p = c.PropStart; p < c.PropStart + c.PropCount; p++)
                if (_history.Props[p].Prop.Id == property.Id)
                {
                    value = _history.Props[p].Next;
                    return true;
                }

            value = default;
            return false;
        }
    }

    /// <summary>A changeset's entities, without copying or boxing anything to walk them.</summary>
    public readonly struct ChangeList : IReadOnlyList<Changed>
    {
        private readonly History? _history;
        private readonly ChangeBuffer? _buffer;
        private readonly int _start;

        internal ChangeList(History history, int start, int count)
        {
            _history = history;
            _buffer = null;
            _start = start;
            Count = count;
        }

        internal ChangeList(ChangeBuffer? buffer)
        {
            _history = null;
            _buffer = buffer;
            _start = 0;
            Count = buffer?.EntityCount ?? 0;
        }

        public int Count { get; }

        public Changed this[int i] => _history != null ? new Changed(_history, _start + i) : new Changed(_buffer!, i);

        public Enumerator GetEnumerator() => new(this);
        IEnumerator<Changed> IEnumerable<Changed>.GetEnumerator() => GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator(ChangeList list) : IEnumerator<Changed>
        {
            private int _i = -1;
            public Changed Current => list[_i];
            object System.Collections.IEnumerator.Current => Current;
            public bool MoveNext() => ++_i < list.Count;
            public void Reset() => _i = -1;
            public void Dispose() { }
        }
    }

    public readonly int Id = id;
    public readonly string ActionName = actionName;
    public long Year { get; internal set; } = year;
    /// <summary>The firing that made these changes (see <c>Database.Firing</c>), 0 if none.</summary>
    public int Firing { get; init; }

    // Open: what it has touched so far. Closed: where its records are in the history.
    internal ChangeBuffer? Buffer;
    internal History? Closed;
    internal int EntityStart;
    internal int EntityCount;

    public ChangeList Changes => Closed != null ? new(Closed, EntityStart, EntityCount) : new(Buffer);

    internal void RecordSet(Database db, EntityId id, EntityTypeId type, PropertyId property, PropertyValue prev) =>
        (Buffer ??= new ChangeBuffer(db)).RecordSet(id, type, property, prev);

    internal void RecordCreate(Database db, EntityId id, EntityTypeId type) =>
        (Buffer ??= new ChangeBuffer(db)).RecordCreate(id, type);
}
