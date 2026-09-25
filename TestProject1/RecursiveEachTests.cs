using Moirai.Parser;

namespace TestProject1;

/// An `each` that is re-entered while it is still walking. Every `each` site kept one results list, so
/// the inner run's FindAll cleared and refilled the list the outer loop was walking: the outer loop then
/// carried on over the inner run's matches (by then none) and skipped the rest of its own. A rule cannot
/// call() itself, but a trigger's body can call an event whose change fires that same trigger again.
public class RecursiveEachTests
{
    private const string Story = @"
entity Thing {
    prop seen: number
}
singleton Counter {
    prop n: number
}
@start
event make {
    create Counter $k: ('k')
    create Thing $a: ('a')
    create Thing $b: ('b')
    create Thing $c: ('c')
}
event bump {
    set #Counter.n = #Counter.n + 1
}
trigger walk {
    when Counter
    each Thing $t: (seen = 0) {
        set $t.seen = 1
        record('{$t.name}')
        call(bump)
    }
}
";

    [Test]
    public void EachLevelWalksWhatItFoundThoughTheBodyReentersTheSameEach()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors.Select(e => e.Message)));
        db.Init();

        db.RunAction("bump");

        // walk finds a, b, c. From a, walk finds b, c; from b, walk finds c; from c, nothing is left. Each
        // level then finishes its own list -- as any each does, it does not re-check the predicate.
        var names = db.Records.Select(r => System.Text.RegularExpressions.Regex.Replace(r.Text, "<[^>]*>", ""));
        Assert.That(string.Join(" ", names), Is.EqualTo("a b c c b c"));
    }
}
