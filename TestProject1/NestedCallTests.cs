using Moirai.Parser;

namespace TestProject1;

/// What happens to an event's own changes when it call()s another event part-way through.
///
/// RunAction opens a fresh changeset for the callee. It used to leave the caller on it, so everything the
/// caller did before the call was dropped from the history and no trigger ever saw it -- w.sg's
/// crown_monarch set the new king's title and the realm's ruler, then called found_settlement, and none
/// of that reached a trigger. The caller now gets its own changeset back when the call returns.
public class NestedCallTests
{
    private const string Story = @"
entity Thing {
    prop x: number
    prop y: number
}
@start
event make {
    create Thing $t: ('thing')
}
event callee {
    pick Thing $t: (y = 0)
    set $t.y = 1
}
event caller {
    pick Thing $t: (x = 0)
    set $t.x = 1
    call(callee)
}
trigger saw_x {
    when Thing and $new.x = 1 and $old.x = 0
    record('saw x')
}
";

    [Test]
    public void TheCallersOwnChangesAreKeptWhenItCallsAnotherEvent()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors.Select(e => e.Message)));
        db.History = new();
        db.Init();

        db.RunAction("caller");

        var thing = db.Entities.Single(e => db.GetEntityType(e.Type).Name == "Thing");
        var x = db.GetPropertyId("Thing", "x");
        var y = db.GetPropertyId("Thing", "y");
        Assert.That(thing.GetProperty(x).IntValue, Is.EqualTo(1));
        // The caller's change is in its own changeset, the callee's in the callee's...
        var caller = db.History!.Changesets.Single(c => c.ActionName == "caller");
        var callee = db.History.Changesets.Single(c => c.ActionName == "callee");
        Assert.That(caller.Changes.Single().Prev.TryGetProperty(x, out _), Is.True);
        Assert.That(callee.Changes.Single().Prev.TryGetProperty(y, out _), Is.True);
        // ...and the trigger watching the caller's change fired, once.
        Assert.That(db.Records.Count(r => r.Text == "saw x"), Is.EqualTo(1));
    }

    [Test]
    public void WithoutTheCallTheSameChangeIsLoggedAndTheTriggerFires()
    {
        // The control for the test above: same story, the call removed.
        var db = StoryParser.Parse(Story.Replace("    call(callee)", ""), out var errors);
        Assert.That(errors, Is.Empty);
        db.History = new();
        db.Init();

        db.RunAction("caller");

        Assert.That(db.History!.Changesets.Any(c => c.ActionName == "caller"), Is.True);
        Assert.That(db.Records.Select(r => r.Text), Has.Member("saw x"));
    }
}
