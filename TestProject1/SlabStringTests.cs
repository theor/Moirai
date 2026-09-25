using System.Runtime.CompilerServices;
using Moirai.Parser;

namespace TestProject1;

/// Strings a pass makes -- a name, a `var` -- live in the world's char slab rather than in a string of their
/// own (see PropertyValue). Whatever holds the text, a string is its text.
public class SlabStringTests
{
    // Every Property, history record and slab is sized by this. The slab offset was meant to fit in what
    // used to be padding; if it ever stops fitting, all of them grow with it.
    [Test]
    public void APropertyValueIsStill24Bytes() =>
        Assert.That(Unsafe.SizeOf<PropertyValue>(), Is.EqualTo(24));

    [Test]
    public void ATextMadeDuringAPassEqualsTheSameLiteral()
    {
        var db = StoryParser.Parse(@"
entity Thing {
    prop label: string
}
@start
event make {
    create Thing $t: ('thing {7}')
    var $n: 7
    set $t.label = 'n{$n}'
    if $t.label = 'n7' {
        record('equal')
    }
    if $t.label != 'n8' {
        record('different')
    }
    if $t.name = 'thing 7' {
        record('named')
    }
}
", out var errors);
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors.Select(e => e.Message)));
        db.Init();

        Assert.That(db.Records.Select(r => r.Text), Is.EqualTo(new[] { "equal", "different", "named" }));
        var thing = db.Entities.Single(e => db.GetEntityType(e.Type).Name == "Thing");
        Assert.That(thing.GetProperty(db.GetPropertyId("Thing", "label")).Value, Is.EqualTo("n7"));
        Assert.That(thing.GetProperty(Database.PropName).Value, Is.EqualTo("thing 7"));
    }
}
