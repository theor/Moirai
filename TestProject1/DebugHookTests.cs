using Moirai.Core;
using Moirai.Parser;

namespace TestProject1;

/// <summary>
/// Verifies the engine-side debugging instrumentation (Phase 1): every executed statement
/// carries a valid source span, execution is reported through <see cref="IDebugHook"/> with a
/// balanced/nesting logical frame stack, and value-stack slots resolve back to <c>$var</c> names.
/// </summary>
public class DebugHookTests
{
    // One recorded statement stop: which frame we were in, the source span, and the
    // variables visible at that point (resolved name -> whether a value was present).
    private sealed record Stop(
        string FrameName,
        DebugFrameKind FrameKind,
        string[] FrameStack,
        SourceSpan Source,
        Dictionary<string, bool> Variables);

    // Records everything the engine reports and resolves variables exactly the way a DAP
    // server would: innermost lexical scope at the stopped instruction, then visible slots.
    private sealed class RecordingHook : IDebugHook
    {
        private readonly List<(DebugFrameKind Kind, string Name, DebugScope? Scope, int Offset)> _frames = new();
        public readonly List<Stop> Stops = new();
        public int MaxDepth;
        public int EnterCount;
        public int ExitCount;

        public void OnEnterFrame(DebugFrameKind kind, string name, DebugScope? scope, int valueOffset)
        {
            _frames.Add((kind, name, scope, valueOffset));
            EnterCount++;
            if (_frames.Count > MaxDepth) MaxDepth = _frames.Count;
        }

        public void OnExitFrame()
        {
            ExitCount++;
            // Stack must never underflow: an exit always has a matching enter.
            Assert.That(_frames.Count, Is.GreaterThan(0), "OnExitFrame without a current frame");
            _frames.RemoveAt(_frames.Count - 1);
        }

        public void OnStatement(IInstruction instruction, ExecuteContext ctx)
        {
            Assert.That(_frames.Count, Is.GreaterThan(0), "OnStatement outside any frame");
            var top = _frames[^1];

            var vars = new Dictionary<string, bool>();
            if (top.Scope != null && instruction.Source.IsValid)
            {
                var innermost = top.Scope.Innermost(instruction.Source);
                foreach (var (slot, name) in innermost.VisibleVariables())
                    vars[name] = ctx.TryGetLocal(slot, out _);
            }

            Stops.Add(new Stop(
                top.Name,
                top.Kind,
                _frames.Select(f => f.Name).ToArray(),
                instruction.Source,
                vars));
        }
    }

    // Deterministic story (same shape as ProfilerTests): spawn + age_up every year,
    // on_birth fires per Person, on_death never matches.
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
}
trigger on_death {
    when Person and $new.alive = false
    record('died')
}";

    [Test]
    public void ReportsSourceFramesAndVariables()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));

        var hook = new RecordingHook();
        db.History = new();
        db.DebugHook = hook;          // attach BEFORE Init so @start is observed too
        db.Init();
        db.Ctx.PassYears(3, true);

        Assert.That(hook.Stops, Is.Not.Empty, "no statements were observed");

        // Frames are balanced.
        Assert.That(hook.ExitCount, Is.EqualTo(hook.EnterCount), "unbalanced enter/exit");

        // Every executed statement maps to a real source line within the story.
        int storyLines = Story.Split('\n').Length;
        foreach (var s in hook.Stops)
        {
            Assert.That(s.Source.IsValid, Is.True, $"statement in '{s.FrameName}' has no source span");
            Assert.That(s.Source.StartLine, Is.InRange(0, storyLines - 1), $"line out of range in '{s.FrameName}'");
        }

        // We saw the expected frames with the right kinds.
        Assert.That(hook.Stops.Any(s => s.FrameName == "spawn" && s.FrameKind == DebugFrameKind.Event));
        Assert.That(hook.Stops.Any(s => s.FrameName == "age_up" && s.FrameKind == DebugFrameKind.Event));
        Assert.That(hook.Stops.Any(s => s.FrameName == "on_birth" && s.FrameKind == DebugFrameKind.Trigger));

        // Inside age_up's each-loop, $p resolves to a value.
        var inEach = hook.Stops.First(s => s.FrameName == "age_up" && s.Variables.ContainsKey("$p"));
        Assert.That(inEach.Variables["$p"], Is.True, "$p should have a value inside the each-loop");

        // Inside the on_birth trigger, $new is visible.
        var inTrigger = hook.Stops.First(s => s.FrameName == "on_birth");
        Assert.That(inTrigger.Variables.ContainsKey("$new"), Is.True, "$new should be visible in the trigger");
        Assert.That(inTrigger.Variables["$new"], Is.True, "$new should have a value in the trigger");
    }

    // An event that call()s another event: the callee's frame must nest under the caller's.
    private const string NestedStory = @"
entity Person {
    prop age: number
}
@start
event create_time {
    create Time $t: 'time'
    set $t.year = 0
}
event inner {
    create Person $p: 'p'
}
@frequency(1, EveryXYear, 1)
event outer {
    call(inner, 1)
}";

    [Test]
    public void NestsFramesForCall()
    {
        var db = StoryParser.Parse(NestedStory, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));

        var hook = new RecordingHook();
        db.History = new();
        db.DebugHook = hook;
        db.Init();
        db.Ctx.PassYears(2, true);

        Assert.That(hook.ExitCount, Is.EqualTo(hook.EnterCount), "unbalanced enter/exit");
        Assert.That(hook.MaxDepth, Is.GreaterThanOrEqualTo(2), "call() should produce a nested frame");

        // A statement executed inside `inner` while `outer` was still on the stack.
        Assert.That(
            hook.Stops.Any(s => s.FrameStack.Length >= 2
                                && s.FrameStack[^1] == "inner"
                                && s.FrameStack[^2] == "outer"),
            Is.True,
            "expected an inner-frame statement nested under outer");
    }

    [Test]
    public void NoHookMeansNoObservation()
    {
        // Sanity: with no hook attached the simulation runs unaffected (the gate is null).
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty);
        db.History = new();
        db.Init();
        Assert.That(db.DebugHook, Is.Null);
        Assert.DoesNotThrow(() => db.Ctx.PassYears(3, true));
    }
}
