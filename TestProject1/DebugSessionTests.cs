using System.Collections.Concurrent;
using System.Threading;
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

        var session = new DebugSession();
        db.History = new();
        db.DebugHook = session;
        db.Init();   // @start runs here (mode=Continue, no breakpoints yet) -> no stop

        int bpLine = LineOf(Story, "set $p.alive = true");
        var accepted = session.SetBreakpoints("story.sg", new[] { bpLine });
        Assert.That(accepted, Does.Contain(bpLine));

        var stops = new BlockingCollection<DebugSession.StopInfo>();
        session.Stopped += s => stops.Add(s);

        var done = new ManualResetEventSlim(false);
        Exception? workerError = null;
        var worker = new Thread(() =>
        {
            try { db.Ctx.PassYears(3, true); }
            catch (Exception e) { workerError = e; }
            finally { done.Set(); }
        }) { IsBackground = true };
        worker.Start();

        int stopCount = 0;
        bool sawSpawnFrame = false;
        bool sawPVariable = false;

        // Drive the session: each stop we inspect, then continue, until the run finishes.
        while (!done.Wait(0) || stops.Count > 0)
        {
            if (!stops.TryTake(out var info, 5000))
                break;

            stopCount++;
            Assert.That(info.Reason, Is.EqualTo(DebugSession.StopReason.Breakpoint));
            Assert.That(info.Line, Is.EqualTo(bpLine), "stopped on the wrong line");

            var stack = session.GetStack();
            Assert.That(stack, Is.Not.Empty);
            Assert.That(stack[0].Line, Is.EqualTo(bpLine));
            if (stack[0].Name == "spawn") sawSpawnFrame = true;

            // $p was created on the previous line, so it must be visible & valued here.
            var vars = session.GetVariables(0);
            if (vars.Any(v => v.Name == "$p" && !string.IsNullOrEmpty(v.Value)))
                sawPVariable = true;

            session.Continue();
        }

        Assert.That(done.Wait(5000), Is.True, "simulation thread did not finish");
        Assert.That(workerError, Is.Null, workerError?.ToString());
        Assert.That(stopCount, Is.EqualTo(3), "breakpoint should hit once per year");
        Assert.That(sawSpawnFrame, Is.True, "top frame should be the spawn event");
        Assert.That(sawPVariable, Is.True, "$p should be inspectable at the breakpoint");
    }

    [Test]
    public void StepOverAdvancesToNextStatement()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));

        var session = new DebugSession();
        db.History = new();
        db.DebugHook = session;
        db.Init();

        int createLine = LineOf(Story, "create Person $p");
        int setLine = LineOf(Story, "set $p.alive = true");
        session.SetBreakpoints("story.sg", new[] { createLine });

        var stops = new BlockingCollection<DebugSession.StopInfo>();
        session.Stopped += s => stops.Add(s);

        var done = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            try { db.Ctx.PassYears(1, true); }
            finally { done.Set(); }
        }) { IsBackground = true };
        worker.Start();

        // First stop: the breakpoint on `create Person`.
        Assert.That(stops.TryTake(out var first, 5000), Is.True);
        Assert.That(first.Reason, Is.EqualTo(DebugSession.StopReason.Breakpoint));
        Assert.That(first.Line, Is.EqualTo(createLine));

        // Step over -> next statement in the same event is `set $p.alive`.
        session.StepOver();
        Assert.That(stops.TryTake(out var second, 5000), Is.True);
        Assert.That(second.Reason, Is.EqualTo(DebugSession.StopReason.Step));
        Assert.That(second.Line, Is.EqualTo(setLine));

        session.Continue();
        Assert.That(done.Wait(5000), Is.True, "simulation thread did not finish");
    }
}
