namespace Moirai.Core;

/// <summary>
/// A set of entity ids kept in ascending order in one array. It is what the query indexes hold: ids are
/// handed out in increasing order, so nearly every add lands at the end, and a scan reads the set as a
/// span with no enumerator to allocate. It replaced <see cref="SortedSet{T}"/>, which paid a tree node
/// per add and a stack per enumeration.
/// </summary>
internal sealed class IdSet
{
    private uint[] _items = Array.Empty<uint>();

    public int Count { get; private set; }

    /// <summary>
    /// The ids, ascending: a view of the set's array, valid until the set is next written. A scan reads it
    /// while only predicates run, and predicates do not write (an <c>each</c> collects its matches before
    /// running any body).
    /// </summary>
    public Ids Ids => new(_items, 0, Count);

    public void Add(uint id)
    {
        int at;
        if (Count == 0 || _items[Count - 1] < id)
            at = Count;
        else
        {
            at = Array.BinarySearch(_items, 0, Count, id);
            if (at >= 0)
                return;
            at = ~at;
        }

        if (Count == _items.Length)
            Array.Resize(ref _items, Math.Max(4, Count * 2));
        if (at < Count)
            Array.Copy(_items, at, _items, at + 1, Count - at);
        _items[at] = id;
        Count++;
    }

    public void Remove(uint id)
    {
        int at = Array.BinarySearch(_items, 0, Count, id);
        if (at < 0)
            return;
        Array.Copy(_items, at + 1, _items, at, Count - at - 1);
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
