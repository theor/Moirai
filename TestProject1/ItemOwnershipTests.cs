using Moirai.Parser;

namespace TestProject1;

/// <summary>
/// Regression for the item_change_owner ownership-history bug: the trigger created the new
/// ItemOwnership record before closing the previous one, so the "close the open record" pick could
/// match (and close) the just-created record at random — leaving the real previous owner's record
/// open forever and writing a bogus same-year record. Closing before creating fixes it.
/// </summary>
public class ItemOwnershipTests
{
    private const string Story = @"
entity Person {
    prop alive: bool
}
entity Item {
    prop owner: Person
}
entity ItemOwnership {
    prop item: Item
    prop owner: Person
    prop start_year: number
    prop end_year: number
}
@start
event setup {
    create Time $t: 'time'
    set $t.year = 0
    create Person $a
    create Item $i: 'sword' {
        owner := $a
    }
}
@frequency(1, EveryXYear, 1)
event handover {
    create Person $p
    pick Item $i: (true)
    set $i.owner = $p
}
trigger item_created {
    when_created Item
    create ItemOwnership $on
    set $on.owner = $new.owner
    set $on.item = $new
    set $on.start_year = #Time.year
}
trigger item_change_owner {
    when Item and owner != $old.owner
    if (pick ItemOwnership $o: (item = $old and end_year = 0)) {
        set $o.end_year = #Time.year
    }
    create ItemOwnership $on
    set $on.owner = $new.owner
    set $on.item = $new
    set $on.start_year = #Time.year
}";

    [Test]
    public void OwnershipRecordsFormAConsistentChain()
    {
        const int years = 5;
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        db.History = new();
        db.Init();                       // setup: item created with owner A (1 open record)
        db.Ctx.PassYears(years, true);   // one owner change per year

        var ownershipType = db.GetEntityType("ItemOwnership").Id;
        var startProp = db.GetPropertyId("ItemOwnership", "start_year");
        var endProp = db.GetPropertyId("ItemOwnership", "end_year");

        var records = db.Entities.Where(e => e.Type == ownershipType).ToList();

        // One record from setup + one per yearly handover.
        Assert.That(records.Count, Is.EqualTo(1 + years));

        int open = 0;
        foreach (var r in records)
        {
            int start = r.GetProperty(startProp).IntValue;
            int end = r.GetProperty(endProp).IntValue;
            if (end == 0)
                open++;
            else
                // A closed record must end strictly after it began — never closed in its own
                // creation year (that was the bug: a record closed at its own start_year).
                Assert.That(end, Is.GreaterThan(start),
                    $"ownership record closed in its own start year (start={start}, end={end})");
        }

        // Exactly one owner at a time: precisely one open (end_year == 0) record.
        Assert.That(open, Is.EqualTo(1), "there must be exactly one open ownership record");

        // The closed records tile the timeline contiguously: every year in [0, years) is covered by
        // exactly one record, and the open record covers the final year onward.
        var closedEnds = records
            .Select(r => (start: r.GetProperty(startProp).IntValue, end: r.GetProperty(endProp).IntValue))
            .Where(t => t.end != 0)
            .OrderBy(t => t.start)
            .ToList();
        for (int i = 1; i < closedEnds.Count; i++)
            Assert.That(closedEnds[i].start, Is.EqualTo(closedEnds[i - 1].end),
                "each record should start exactly when the previous one ended (no gaps/overlaps)");
    }
}
