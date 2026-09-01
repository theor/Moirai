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

    [Test]
    public void BreakpointInsideFunctionHits()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        int bpLine = LineOf(Story, "record('made')");

        using var run = DebugRun.Start(db, 1, "s.sg", bpLine);

        var info = run.NextStop("breakpoint inside a function");
        Assert.That(info.Reason, Is.EqualTo(DebugSession.StopReason.Breakpoint));
        Assert.That(info.Line, Is.EqualTo(bpLine));
        var stack = run.Session.GetStack();
        Assert.That(stack[0].Name, Is.EqualTo("make_country"));
        Assert.That(stack.Any(f => f.Name == "run"), Is.True, "caller frame should be on the stack");
        run.Drain();
    }

    [Test]
    public void StepIntoFunction()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        int callLine = LineOf(Story, "call(make_country, 1)");
        int firstBodyLine = LineOf(Story, "create Country $c");

        using var run = DebugRun.Start(db, 1, "s.sg", callLine);

        run.NextStop("the call site");
        run.Session.StepIn();
        var stepped = run.NextStop("step-in");
        Assert.That(stepped.Reason, Is.EqualTo(DebugSession.StopReason.Step));
        Assert.That(stepped.Line, Is.EqualTo(firstBodyLine), "step-in should land on the first body statement");
        Assert.That(run.Session.GetStack()[0].Name, Is.EqualTo("make_country"));
        run.Drain();
    }
}
