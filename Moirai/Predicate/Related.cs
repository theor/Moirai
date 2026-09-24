using Moirai.Core;

/// <summary>
/// <c>related($a, $b, n)</c>: true when <c>$a</c> and <c>$b</c> share an ancestor at depths <c>i</c> and
/// <c>j</c> with <c>i + j &lt;= n</c>, each counting as their own ancestor at depth 0. That is the civil-law
/// degree of kinship: a parent is 1, a grandparent or a sibling 2, an aunt, uncle or great-grandparent 3, a
/// first cousin 4. Parents are the type's <c>parent1</c>/<c>parent2</c>, resolved when the story is parsed.
///
/// <para>Written for the inside of a pick, where one side is fixed and the other runs over every candidate.
/// The fixed side's ancestors are gathered once and kept until the world is written to (nothing is, during
/// a scan); each candidate then walks up at most <c>n</c> generations into a stack buffer and stops at the
/// first shared ancestor close enough. No allocation per candidate, and founders — who have no parents —
/// cost one lookup.</para>
/// </summary>
public sealed class Related : IValueCall, IValueSql
{
    /// <summary>Deepest degree accepted: 2^(n+1) - 1 ancestors per side is 127 at 6, still a stack buffer.</summary>
    public const int MaxDegree = 6;

    public readonly IValue A, B;
    public readonly int Degree;
    private readonly PropertyId _parent1, _parent2;

    // One side's ancestors (id, depth), kept across candidates. Keyed on the entity and the world's write
    // version, so a write — a birth setting parent1, say — can never leave it describing an old family.
    private readonly uint[] _ids;
    private readonly byte[] _depths;
    private int _count;
    private uint _cachedFor;
    private long _cachedVersion = -1;

    public Related(IValue a, IValue b, int degree, PropertyId parent1, PropertyId parent2)
    {
        if (degree < 0 || degree > MaxDegree)
            throw new ArgumentOutOfRangeException(nameof(degree));
        A = a;
        B = b;
        Degree = degree;
        _parent1 = parent1;
        _parent2 = parent2;
        _ids = new uint[Capacity(degree)];
        _depths = new byte[Capacity(degree)];
    }

    private static int Capacity(int degree) => (1 << (degree + 1)) - 1;

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var a = A.Compute(ctx).Id;
        var b = B.Compute(ctx).Id;
        if (a.IsNull || b.IsNull)
            return false;
        if (a.Id == b.Id)
            return true;

        var db = ctx.Database;
        if (a.Id != _cachedFor || db.WriteVersion != _cachedVersion)
        {
            _count = Ancestors(db, a.Id, _ids, _depths);
            _cachedFor = a.Id;
            _cachedVersion = db.WriteVersion;
        }

        // Walk b's side breadth-first, so the closest ancestors are tried first.
        Span<uint> ids = stackalloc uint[_ids.Length];
        Span<byte> depths = stackalloc byte[_ids.Length];
        ids[0] = b.Id;
        depths[0] = 0;
        int count = 1;
        for (int head = 0; head < count; head++)
        {
            var id = ids[head];
            var j = depths[head];
            for (int k = 0; k < _count; k++)
                if (_ids[k] == id && _depths[k] + j <= Degree)
                    return true;

            if (j < Degree)
                Push(db, id, (byte)(j + 1), ids, depths, ref count);
        }

        return false;
    }

    // Everyone up to Degree generations above `id`, including itself at depth 0.
    private int Ancestors(Database db, uint id, Span<uint> ids, Span<byte> depths)
    {
        ids[0] = id;
        depths[0] = 0;
        int count = 1;
        for (int head = 0; head < count; head++)
            if (depths[head] < Degree)
                Push(db, ids[head], (byte)(depths[head] + 1), ids, depths, ref count);
        return count;
    }

    private void Push(Database db, uint child, byte depth, Span<uint> ids, Span<byte> depths, ref int count)
    {
        if (!db.TryGetEntity(new EntityId(child), out var e))
            return;
        foreach (var prop in (ReadOnlySpan<PropertyId>)[_parent1, _parent2])
        {
            if (!e.TryGetProperty(prop, out var p) || p.Id.IsNull)
                continue;
            ids[count] = p.Id.Id;
            depths[count] = depth;
            count++;
        }
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return A;
        yield return B;
        yield return new Literal(Degree);
    }
}
