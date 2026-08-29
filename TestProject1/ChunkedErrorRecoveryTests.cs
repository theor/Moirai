using Moirai.Parser;

namespace TestProject1;

/// Phase 4 of the ANTLR->Superpower migration (see
/// C:\Users\theor\.claude\plans\stateful-dancing-stroustrup.md): StoryParser.Parse chunks the token
/// stream at top-level def boundaries so one syntax error doesn't blank out the whole file, and the
/// LSP gets one diagnostic per broken def rather than just the first. An accepted v1 limitation:
/// multiple syntax errors *inside* one large def still collapse to a single diagnostic for that chunk.
public class ChunkedErrorRecoveryTests : TestsBase
{
    [Test]
    public void SingleBrokenDef_StillRegistersEverythingElse()
    {
        // The middle event is missing its closing '}' -- broken.
        const string s = @"
entity Person {
    prop age: number
}
event good_one {
    record('a')
}
event broken {
    record('b')
event also_good {
    record('c')
}";
        var db = StoryParser.Parse(s, out var errors);

        Assert.That(errors.Count(e => e.Severity == StoryParser.Severity.Error), Is.GreaterThan(0));
        // Both well-formed events, on either side of the broken one, still registered.
        Assert.That(db.Actions.Select(a => a.Name), Does.Contain("good_one").And.Contain("also_good"));
        Assert.That(db.Actions.Select(a => a.Name), Has.None.EqualTo("broken"));
        // The entity (before any event) is also unaffected.
        Assert.That(db.Types.Select(t => t.Name), Does.Contain("Person"));
    }

    [Test]
    public void TwoIndependentlyBrokenDefs_ProduceTwoErrors()
    {
        const string s = @"
event first_broken {
    record('a')
event second_broken {
    record('b')
event good {
    record('c')
}";
        var db = StoryParser.Parse(s, out var errors);
        var hardErrors = errors.Where(e => e.Severity == StoryParser.Severity.Error).ToList();

        Assert.That(hardErrors, Has.Count.EqualTo(2), string.Join("\n", hardErrors));
        Assert.That(db.Actions.Select(a => a.Name), Does.Contain("good"));
    }

    [Test]
    public void WellFormedFile_StillProducesZeroErrors_ChunkingIsNoOpForValidInput()
    {
        const string s = @"
entity Person {
    prop age: number
}
event e {
    create Person $p: 'x'
}
trigger t {
    when_created Person
    record('created')
}";
        var db = StoryParser.Parse(s, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        Assert.That(db.Types.Select(t => t.Name), Does.Contain("Person"));
        Assert.That(db.Actions.Select(a => a.Name), Does.Contain("e"));
        Assert.That(db.Triggers.Select(t => t.Name), Does.Contain("t"));
    }

    [Test]
    public void TokenizerLevelError_DoesNotCrashParse()
    {
        // A bare '"' is a tokenizer-level error (no DOUBLE_QUOTE rule) -- must be recorded, not thrown,
        // and must not prevent the rest of the file from registering.
        const string s = @"
event bad_token {
    record(""oops"")
}
event fine {
    record('ok')
}";
        Database db = null!;
        List<StoryParser.Error> errors = null!;
        Assert.DoesNotThrow(() => db = StoryParser.Parse(s, out errors));
        Assert.That(errors, Is.Not.Empty);
        Assert.That(db.Actions.Select(a => a.Name), Does.Contain("fine"));
    }
}
