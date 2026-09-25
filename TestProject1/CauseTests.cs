using Moirai.Api;

namespace TestProject1;

/// "Why did this happen?": every record carries the firing of the rule that wrote it, and
/// WorldSession.GetCause walks from there back to the event the schedule started. The log is
/// bookkeeping only -- the records and history a world produces are unchanged (the corpus goldens pin
/// that) -- so these tests are about the chain it reports.
public class CauseTests
{
    private const string Story = @"
entity Person {
    prop alive: bool
    prop title: number
}
@start
event begin {
    create Time $t: 'time' {
        year := 100
    }
    create Person $p: ('Aldric') {
        alive := true
    }
}
@tag('crime')
event murder {
    pick Person $v: (alive)
    set $v.alive = false
    record('{$v.name} is murdered')
}
@tag('politics')
trigger mourning {
    when Person and $old.alive = true and $new.alive = false
    record('the realm mourns {$new.name}')
}
event feast {
    record('a feast is held')
}
event crown {
    pick Person $p: (alive)
    set $p.title = 1
    call(feast)
}
event prophecy {
    pick Person $p: (alive)
    schedule($p, #Time.year + 5) {
        record('the prophecy comes true for {$self.name}')
    }
}
";

    private static WorldSession Session() => new(Story);

    private static Database.Record RecordOf(WorldSession s, string text) =>
        s.Database.Records.Single(r => r.Text.Contains(text));

    [Test]
    public void ATriggersRecordTracesToTheChangeThatSetItOffAndTheEventThatMadeIt()
    {
        var s = Session();
        s.RunAction(s.Database.Actions.First(a => a.Name == "murder").Id);

        var mourning = RecordOf(s, "the realm mourns");
        var steps = s.GetCause(mourning.Firing).Steps;

        Assert.That(steps.Select(x => (x.Kind, x.Rule)),
            Is.EqualTo(new[] { ("trigger", "mourning"), ("event", "murder") }));
        Assert.That(steps[0].Because, Does.Match(@"^<#\d+>Aldric</>: alive true -> false$"));
        Assert.That(steps[0].Line, Is.EqualTo(Story.Split('\n').ToList().FindIndex(l => l.StartsWith("trigger mourning")) + 1));
        Assert.That(steps[1].Because, Is.Empty, "an event is not set off by a change; the schedule ran it");
        Assert.That(steps[1].Records, Has.Some.Contains("is murdered"));
    }

    [Test]
    public void ATriggersRecordIsAttributedToTheTriggerItselfWithItsTags()
    {
        var s = Session();
        s.RunAction(s.Database.Actions.First(a => a.Name == "murder").Id);

        var mourning = RecordOf(s, "the realm mourns");
        Assert.That(mourning.Rule, Is.EqualTo("mourning"));
        Assert.That(mourning.Tags, Is.EqualTo(new[] { "'politics'" }), "the trigger's tags, not the event's");
        // ActionId stays the event: the viewer groups and hides records by event.
        Assert.That(mourning.ActionId, Is.EqualTo(s.Database.Actions.First(a => a.Name == "murder").Id));

        var bio = s.GetBiography(mourning.Participants[0].Id);
        Assert.That(bio.Timeline.Single(e => e.Text.Contains("mourns")).ActionName, Is.EqualTo("mourning"));
    }

    [Test]
    public void ACalledEventTracesToItsCaller()
    {
        var s = Session();
        s.RunAction(s.Database.Actions.First(a => a.Name == "crown").Id);

        var steps = s.GetCause(RecordOf(s, "a feast is held").Firing).Steps;
        Assert.That(steps.Select(x => (x.Kind, x.Rule)), Is.EqualTo(new[] { ("call", "feast"), ("event", "crown") }));
    }

    [Test]
    public void AScheduledBodyTracesToTheRuleThatScheduledItYearsEarlier()
    {
        var s = Session();
        s.RunAction(s.Database.Actions.First(a => a.Name == "prophecy").Id);
        s.PassYears(6);

        var steps = s.GetCause(RecordOf(s, "the prophecy comes true").Firing).Steps;
        Assert.That(steps.Select(x => (x.Kind, x.Rule)),
            Is.EqualTo(new[] { ("scheduled", steps[0].Rule), ("event", "prophecy") }));
        Assert.That(steps[0].Year - steps[1].Year, Is.EqualTo(5));
        Assert.That(steps[0].Line, Is.GreaterThan(0));
    }

    [Test]
    public void AnUnknownFiringHasNoCause()
    {
        var s = Session();
        Assert.That(s.GetCause(0).Steps, Is.Empty);
        Assert.That(s.GetCause(99_999).Steps, Is.Empty);
    }

    [Test]
    public void EveryRecordInAWorldHasACause()
    {
        // Nothing w.sg writes should come out unattributed: that would be a "why?" with no answer.
        var s = new WorldSession(File.ReadAllText(FindWsg()), 42);
        s.PassYears(120);

        Assert.That(s.Database.Records.Where(r => r.Firing == 0), Is.Empty);
        Assert.That(s.Database.Records.All(r => s.GetCause(r.Firing).Steps.Length > 0), Is.True);
    }

    private static string FindWsg()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MoiraiCli", "w.sg");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("MoiraiCli/w.sg");
    }
}
