using Moirai.Parser;

namespace TestProject1;

/// Which changes a `when Changed` trigger is even evaluated against. Two gates, both exact -- they only skip
/// evaluations whose predicate could not have been true:
///   - at least one property the predicate reads was written (GatingProps);
///   - every property of a top-level `$old.p` conjunct was written (RequiredProps), since $old of an
///     unwritten property reads the default. This is what keeps w.sg's canonize_saint, which reads
///     `devotion`, from being re-checked on every devotion write when it can only match on a death.
public class TriggerGatingTests : TestsBase
{
    private const string Story = @"
entity Person {
    prop alive: bool
    prop devotion: number
}
@start
event setup {
    create Person $p {
        alive := true
    }
}
event pray {
    pick Person $p: (alive)
    set $p.devotion = $p.devotion + 10
}
event die {
    pick Person $p: (alive)
    set $p.alive = false
}
trigger saint {
    when Person and alive = false and $old.alive and devotion > 20
    record('saint')
}
trigger devout {
    when Person and devotion > 20
    record('devout')
}
";

    [Test]
    public void AnOldConjunctGatesOnItsOwnProperty()
    {
        var db = Run(Story, out _);
        var saint = db.Triggers.Single(t => t.Name == "saint");
        var devout = db.Triggers.Single(t => t.Name == "devout");
        db.GetTriggerGatingProps(saint); // the gates are worked out on first use
        db.GetTriggerGatingProps(devout);
        Assert.That(db.GetPropertyName(saint.RequiredProps!.Single()), Does.EndWith("alive"));
        Assert.That(devout.RequiredProps, Is.Null, "no $old conjunct, nothing required");

        for (int i = 0; i < 5; i++)
            db.RunAction("pray");
        // Every prayer changed devotion, which both read; only devout is evaluated, since alive never moved.
        Assert.That(saint.Attempts, Is.EqualTo(0));
        Assert.That(devout.Attempts, Is.EqualTo(5));

        db.RunAction("die");
        Assert.That(saint.Attempts, Is.EqualTo(1));
        Assert.That(db.Records.Select(r => r.Text), Has.Member("saint"));
    }

    [Test]
    public void AConjunctThatCannotBeReadKeepsTheTriggerUngated()
    {
        // A function call is not analysed, so the predicate is evaluated on every change -- neither gate
        // may skip a predicate that could draw a random number, or its trigger's stream would move.
        var db = Run(@"
entity Person {
    prop alive: bool
    prop devotion: number
}
function pious($p: Person): bool {
    $p.devotion > 20
}
@start
event setup {
    create Person $p {
        alive := true
    }
}
event pray {
    pick Person $p: (alive)
    set $p.devotion = $p.devotion + 10
}
trigger saint {
    when Person and $old.alive and pious($new)
    record('saint')
}
", out _);
        var saint = db.Triggers.Single(t => t.Name == "saint");
        db.RunAction("pray");
        Assert.That(saint.RequiredProps, Is.Null);
        Assert.That(saint.Attempts, Is.EqualTo(1));
    }
}
