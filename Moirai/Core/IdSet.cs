namespace Moirai.Core;

/// <summary>
/// A set of entity ids kept in ascending order. It is what the query indexes hold: ids are handed out in
/// increasing order, so nearly every add lands at the end, and a scan reads the set as a span with no
/// enumerator to allocate. It replaced <see cref="SortedSet{T}"/>, which paid a tree node per add and a
/// stack per enumeration.
///
/// <para>A struct, held inline in the index's dictionary (write through a ref to the entry), with its ids
/// in a stretch of a shared <see cref="Slab{T}"/>: a new bucket -- a new owner, a new founder -- then
/// costs no object and no array of its own. Growing takes a stretch twice the size and leaves the old one
/// behind, which is at most as much slab as the sets hold.</para>
/// </summary>
internal struct IdSet
{
    private uint[]? _items;
    private int _offset;
    private int _capacity;

    public int Count { readonly get; private set; }

    /// <summary>
    /// The ids, ascending: a view of the set's storage, valid until the set is next written. A scan reads it
    /// while only predicates run, and predicates do not write (an <c>each</c> collects its matches before
    /// running any body).
    /// </summary>
    public readonly Ids Ids => _items == null ? Ids.Empty : new(_items, _offset, Count);

    public void Add(uint id, Slab<uint> slab)
    {
        int at;
        if (Count == 0 || _items![_offset + Count - 1] < id)
            at = Count;
        else
        {
            at = Array.BinarySearch(_items, _offset, Count, id);
            if (at >= 0)
                return;
            at = ~at - _offset;
        }

        if (Count == _capacity)
        {
            int capacity = Math.Max(4, _capacity * 2);
            var (items, offset) = slab.Take(capacity);
            if (Count > 0)
                Array.Copy(_items!, _offset, items, offset, Count);
            (_items, _offset, _capacity) = (items, offset, capacity);
        }

        if (at < Count)
            Array.Copy(_items!, _offset + at, _items!, _offset + at + 1, Count - at);
        _items![_offset + at] = id;
        Count++;
    }

    public void Remove(uint id)
    {
        if (Count == 0)
            return;
        int at = Array.BinarySearch(_items!, _offset, Count, id);
        if (at < 0)
            return;
        Array.Copy(_items!, at + 1, _items!, at, _offset + Count - at - 1);
        Count--;
    }
}

/// <summary>A run of ascending ids in some array: an index bucket, or a query's scratch.</summary>
internal readonly struct Ids(uint[] array, int start, int count)
{
    public readonly uint[] Array = array;
    public readonly int Start = start;
    public readonly int Count = count;
    public ReadOnlySpan<uint> Span => new(Array, Start, Count);
    public static Ids Empty => new(System.Array.Empty<uint>(), 0, 0);
}
