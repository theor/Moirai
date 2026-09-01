using Moirai.Core;
using Moirai.Parser;

namespace TestProject1;

/// <summary>
/// Drives <see cref="DebugSession"/> the way a Debug Adapter would: run the simulation on a
/// worker thread, hit a breakpoint, inspect frames/variables while suspended, then step/continue.
/// </summary>
public class DebugSessionTests
{
    private const string Story = @"
entity Person {
    prop alive: bool
    prop age: number
}
@start
event create_time {
    create Time $t: 'time'
    set $t.year = 0
}
@frequency(1, EveryXYear, 1)
event spawn {
    create Person $p: 'p'
    set $p.alive = true
}
@frequency(1, EveryXYear, 1)
event age_up {
    each Person $p: (alive = true) {
        set $p.age = $p.age + 1
    }
}
trigger on_birth {
    when_created Person
    set $new.age = 0
}";

    // 1-based line number of the first line containing needle (matches DAP line numbering).
    private static int LineOf(string text, string needle)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains(needle))
                return i + 1;
        Assert.Fail($"needle not found: {needle}");
        return -1;
    }

    [Test]
    public void BreakpointStopsWithFramesAndVariables()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));

        int bpLine = LineOf(Story, "set $p.alive = true");

        // @start runs inside Init, before any breakpoint exists, so it cannot stop.
        using var run = DebugRun.Start(db, 3, "story.sg", bpLine);
        Assert.That(run.AcceptedBreakpoints, Does.Contain(bpLine));

        bool sawSpawnFrame = false;
        bool sawPVariable = false;

        // Exactly one hit per year, so the count is a loop bound rather than something to discover by
        // waiting for stops to stop coming. Whatever the run does after those three is caught by the
        // emptiness check below, and neither depends on how promptly the worker is scheduled.
        for (int year = 1; year <= 3; year++)
        {
            var info = run.NextStop($"breakpoint hit {year} of 3");
            Assert.That(info.Reason, Is.EqualTo(DebugSession.StopReason.Breakpoint));
            Assert.That(info.Line, Is.EqualTo(bpLine), "stopped on the wrong line");

            var stack = run.Session.GetStack();
            Assert.That(stack, Is.Not.Empty);
            Assert.That(stack[0].Line, Is.EqualTo(bpLine));
            if (stack[0].Name == "spawn") sawSpawnFrame = true;

            // $p was created on the previous line, so it must be visible & valued here.
            var vars = run.Session.GetVariables(0);
            if (vars.Any(v => v.Name == "$p" && !string.IsNullOrEmpty(v.Value)))
                sawPVariable = true;

            run.Session.Continue();
        }

        run.Drain();
        Assert.That(run.Stops, Is.Empty, "the breakpoint should hit once per year and no more");
        Assert.That(sawSpawnFrame, Is.True, "top frame should be the spawn event");
        Assert.That(sawPVariable, Is.True, "$p should be inspectable at the breakpoint");
    }

    [Test]
    public void EntityVariablesExpandToProperties()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));

        // Break inside age_up's each-loop, where $p is an existing Person with alive/age set.
        int bpLine = LineOf(Story, "set $p.age = $p.age + 1");
        using var run = DebugRun.Start(db, 2, "story.sg", bpLine);
        var session = run.Session;

        run.NextStop("the breakpoint in age_up");

        // $p is an entity-typed local: it must be expandable.
        var locals = session.GetVariables(0);
        var p = locals.FirstOrDefault(v => v.Name == "$p");
        Assert.That(p, Is.Not.Null, "$p not found");
        Assert.That(p!.VariablesReference, Is.GreaterThan(0), "$p should be expandable");
        Assert.That(p.Value, Does.Contain("Person"), "entity summary should name its type");

        // Expanding $p yields its properties, including the ones set during simulation.
        var props = session.GetVariablesByReference(p.VariablesReference);
        Assert.That(props, Is.Not.Empty);
        var alive = props.FirstOrDefault(v => v.Name == "alive");
        Assert.That(alive, Is.Not.Null, "expanded entity should expose 'alive'");
        Assert.That(alive!.Value.ToLowerInvariant(), Does.Contain("true"));
        Assert.That(props.Any(v => v.Name == "age"), Is.True, "expanded entity should expose 'age'");

        // Drain remaining stops so the run can finish.
        run.Drain();
    }

    [Test]
    public void WorldScopeListsYearAndEntityCounts()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));

        int bpLine = LineOf(Story, "set $p.age = $p.age + 1");
        using var run = DebugRun.Start(db, 2, "story.sg", bpLine);
        var session = run.Session;

        run.NextStop("the breakpoint in age_up");

        var world = session.GetVariablesByReference(session.GetWorldReference());
        var year = world.FirstOrDefault(v => v.Name == "year");
        Assert.That(year, Is.Not.Null, "World should expose year");
        Assert.That(int.Parse(year!.Value), Is.GreaterThanOrEqualTo(1));

        Assert.That(world.Any(v => v.Name == "entities"), Is.True, "World should expose total entity count");

        var person = world.FirstOrDefault(v => v.Name == "Person");
        Assert.That(person, Is.Not.Null, "World should list the Person type");
        Assert.That(int.Parse(person!.Value), Is.GreaterThanOrEqualTo(1), "at least one Person exists");

        run.Drain();
    }

    private const string ScheduleStory = @"
entity Person {
    prop birthdate: number
    prop grown: bool
}
@start
event create_time {
    create Time $t: 'time'
    set $t.year = 100
}
@frequency(1, EveryXYear, 1)
event make {
    create Person $p
    set $p.birthdate = #Time.year
}
trigger born {
    when_created Person
    schedule($new, $new.birthdate + 1) {
        set $self.grown = true
        record('grew')
    }
}";

    [Test]
    public void ScheduledBodyExposesSelf()
    {
        var db = StoryParser.Parse(ScheduleStory, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));

        int bpLine = LineOf(ScheduleStory, "set $self.grown = true");
        using var run = DebugRun.Start(db, 3, "s.sg", bpLine);
        var session = run.Session;

        var info = run.NextStop("the breakpoint in a scheduled body");
        Assert.That(info.Line, Is.EqualTo(bpLine));

        var stack = session.GetStack();
        Assert.That(stack[0].Name, Does.StartWith("schedule@"), "top frame should be the scheduled body");

        // The deferred body binds $self — it must be visible and valued (an entity).
        var locals = session.GetVariables(0);
        var self = locals.FirstOrDefault(v => v.Name == "$self");
        Assert.That(self, Is.Not.Null, "$self should be visible in a scheduled body");
        Assert.That(self!.Value, Does.Contain("Person"), "$self should resolve to the bound entity");
        Assert.That(self.VariablesReference, Is.GreaterThan(0), "$self should be expandable");

        run.Drain();
    }

    [Test]
    public void StepOverAdvancesToNextStatement()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));

        int createLine = LineOf(Story, "create Person $p");
        int setLine = LineOf(Story, "set $p.alive = true");
        using var run = DebugRun.Start(db, 1, "story.sg", createLine);

        // First stop: the breakpoint on `create Person`.
        var first = run.NextStop("the breakpoint on `create Person`");
        Assert.That(first.Reason, Is.EqualTo(DebugSession.StopReason.Breakpoint));
        Assert.That(first.Line, Is.EqualTo(createLine));

        // Step over -> next statement in the same event is `set $p.alive`.
        run.Session.StepOver();
        var second = run.NextStop("step-over");
        Assert.That(second.Reason, Is.EqualTo(DebugSession.StopReason.Step));
        Assert.That(second.Line, Is.EqualTo(setLine));

        run.Drain();
    }
}
