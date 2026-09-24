using Moirai.Api;
using Moirai.Parser;

namespace TestProject1;

/// The clock is the one thing every world needs, so it always exists: a story may create Time, set its
/// year, or never mention it, and a pass works in all three cases. Before this, a story that forgot to
/// create Time parsed clean and threw "missing Time entity" on its first pass -- and in the browser, a
/// draft like that locked the app on "Starting the engine" at every reload.
public class TimeSingletonTests
{
    private static Database Init(string story)
    {
        var db = StoryParser.Parse(story, out var errors);
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors));
        db.History = new();
        db.Init();
        return db;
    }

    private static int TimeCount(Database db) =>
        db.Entities.Count(e => db.GetEntityType(e.Type).Name == "Time");

    [Test]
    public void AStoryThatNeverMentionsTimeGetsAClockAtYearZero()
    {
        var db = Init("entity Hero {}\n@start\nevent begin {\n    create Hero $h: ('Ann')\n}\n");

        // Nothing needs the clock before the first pass, so it is not made until then.
        Assert.That(TimeCount(db), Is.EqualTo(0));
        Assert.That(db.StartYear, Is.EqualTo(0));
        db.Ctx.PassYears(10, true);
        Assert.That(TimeCount(db), Is.EqualTo(1));
        Assert.That(db.Ctx.Year, Is.EqualTo(10));
    }

    [Test]
    public void SettingTheYearIsEnoughToBringTheClockIntoBeing()
    {
        var db = Init("@start\nevent begin {\n    set #Time.year = 764\n}\n");

        Assert.That(TimeCount(db), Is.EqualTo(1));
        Assert.That(db.StartYear, Is.EqualTo(764));
    }

    [Test]
    public void CreatingTimeAfterItExistsBindsTheSameInstance()
    {
        // The first event's write brings Time into being; the second's create must not make another.
        var db = Init("@start\nevent a {\n    set #Time.year = 700\n}\n" +
                      "@start\nevent b {\n    create Time $t: 'time' {\n        year := 764\n    }\n}\n");

        Assert.That(TimeCount(db), Is.EqualTo(1));
        Assert.That(db.StartYear, Is.EqualTo(764));
    }

    [Test]
    public void AStoryThatCreatesTimeFirstKeepsItsEntityIds()
    {
        // w.sg creates Time before anything else, so Time stays entity 1 and nothing else moves: links,
        // selections and the goldens all name entities by id.
        var db = Init("entity Hero {}\n@start\nevent begin {\n    create Time $t: 'time' {\n        year := 764\n    }\n" +
                      "    create Hero $h: ('Ann')\n}\n");

        Assert.That(TimeCount(db), Is.EqualTo(1));
        Assert.That(db.Entities.First().Id.Id, Is.EqualTo(1));
        Assert.That(db.GetEntityType(db.Entities.First().Type).Name, Is.EqualTo("Time"));
    }

    [Test]
    public void AStoryWithoutTimeCanBeAppliedAndPassed()
    {
        var s = new WorldSession("entity Hero {}\n@start\nevent begin {\n    create Hero $h: ('Ann')\n}\n");
        s.PassYears(5);
        Assert.That(s.Year, Is.EqualTo(5));
    }
}
