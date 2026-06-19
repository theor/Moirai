using System.Collections.Concurrent;
using System.Threading;
using Moirai.Core;
using Moirai.Parser;

namespace TestProject1;

/// <summary>
/// Step-through debugging across function boundaries: breakpoints inside function bodies
/// (called via <c>call(..)</c> and directly) and stepping into a function.
/// </summary>
public class DebugFunctionTests
{
    private const string Story = @"
entity Country {
    prop founded: number
}
function make_country() {
    create Country $c: 'c'
    record('made')
}
@start
event setup {
    create Time $t: 'time'
    set $t.year = 0
}
@frequency(1, EveryXYear, 1)
event run {
    call(make_country, 1)
}";

    private static int LineOf(string text, string needle)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains(needle)) return i + 1;
        return -1;
    }

    private static (DebugSession session, ManualResetEventSlim done, BlockingCollection<DebugSession.StopInfo> stops)
        StartRun(Database db, int years)
    {
        var session = new DebugSession();
        db.History = new();
        db.DebugHook = session;
        db.Init();
        var stops = new BlockingCollection<DebugSession.StopInfo>();
        session.Stopped += s => stops.Add(s);
        var done = new ManualResetEventSlim(false);
        new Thread(() => { try { db.Ctx.PassYears(years, true); } finally { done.Set(); } })
            { IsBackground = true }.Start();
        return (session, done, stops);
    }

    private static void Drain(DebugSession session, ManualResetEventSlim done, BlockingCollection<DebugSession.StopInfo> stops)
    {
        session.Continue();
        while (!done.Wait(50))
        {
            if (stops.TryTake(out _, 1000)) session.Continue();
            else break;
        }
        Assert.That(done.Wait(3000), Is.True);
    }

    [Test]
    public void BreakpointInsideFunctionHits()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        int bpLine = LineOf(Story, "record('made')");

        var (session, done, stops) = StartRun(db, 1);
        session.SetBreakpoints("s.sg", new[] { bpLine });   // set after Init runs @start; run hits it

        Assert.That(stops.TryTake(out var info, 5000), Is.True, "breakpoint inside function never hit");
        Assert.That(info.Reason, Is.EqualTo(DebugSession.StopReason.Breakpoint));
        Assert.That(info.Line, Is.EqualTo(bpLine));
        var stack = session.GetStack();
        Assert.That(stack[0].Name, Is.EqualTo("make_country"));
        Assert.That(stack.Any(f => f.Name == "run"), Is.True, "caller frame should be on the stack");
        Drain(session, done, stops);
    }

    [Test]
    public void StepIntoFunction()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        int callLine = LineOf(Story, "call(make_country, 1)");
        int firstBodyLine = LineOf(Story, "create Country $c");

        var (session, done, stops) = StartRun(db, 1);
        session.SetBreakpoints("s.sg", new[] { callLine });

        Assert.That(stops.TryTake(out _, 5000), Is.True, "did not stop at call site");
        session.StepIn();
        Assert.That(stops.TryTake(out var stepped, 5000), Is.True, "step-in produced no stop");
        Assert.That(stepped.Reason, Is.EqualTo(DebugSession.StopReason.Step));
        Assert.That(stepped.Line, Is.EqualTo(firstBodyLine), "step-in should land on the first body statement");
        Assert.That(session.GetStack()[0].Name, Is.EqualTo("make_country"));
        Drain(session, done, stops);
    }
}
