namespace Moirai.Core;

public record struct TableDefinitionId(int Id);

/// <summary>
/// A reusable named weighted-random table, declared in the DSL as
/// <c>table Name { 70 =&gt; value, 30 =&gt; value }</c> (or with implicit equal weight,
/// <c>table Name { value, value }</c>) and sampled with <c>roll(Name)</c>.
/// Unlike inline <c>random_weighted</c> (which dispatches to effect bodies), a table
/// holds value expressions and <see cref="Roll"/> returns the selected value.
/// </summary>
public class TableDefinition
{
    public readonly int Id;
    public readonly string Name;
    // Entries sorted by cumulative weight (the exclusive upper bound of each band).
    public readonly (int Cumulative, IValue Value)[] Entries;
    public readonly int TotalWeight;
    // Static type reported as roll(...)'s return type — taken from the first entry (best effort
    // for heterogeneous tables, which the author is responsible for keeping sensible).
    public readonly PropertyValue.ValueType ValueType;

    public TableDefinition(int id, string name, (int Weight, IValue Value)[] weighted,
        PropertyValue.ValueType valueType)
    {
        Id = id;
        Name = name;
        ValueType = valueType;
        Entries = new (int, IValue)[weighted.Length];
        int acc = 0;
        for (int i = 0; i < weighted.Length; i++)
        {
            acc += weighted[i].Weight;
            Entries[i] = (acc, weighted[i].Value);
        }

        TotalWeight = acc;
    }

    public PropertyValue Roll(ExecuteContext ctx)
    {
        if (TotalWeight <= 0 || Entries.Length == 0)
            return default;
        var r = ctx.Rnd.GenerateNext((uint)TotalWeight);
        for (int i = 0; i < Entries.Length; i++)
            if (r < Entries[i].Cumulative)
                return Entries[i].Value.Compute(ctx);
        return Entries[^1].Value.Compute(ctx);
    }
}
