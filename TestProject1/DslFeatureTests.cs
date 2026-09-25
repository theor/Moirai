using Moirai.Parser;

namespace TestProject1;

/// The DSL additions from docs/dsl-usability.md: aggregates over a query, `pick ... else`, `chance()`,
/// `random_weighted` without a total, entity comparison, a typed `id`, and block comments. Every story
/// here goes through TestsBase.Run, which also checks the printer writes back a story that reparses to
/// the same text.
public class DslFeatureTests : TestsBase
{
    private const string Things = @"
entity Place {
    prop size: number
}
entity Thing {
    prop x: number
    prop p: percentage
    prop f: float
    prop alive: bool
    prop place: Place
}
";

    // A value read off an entity is written as a link to it (<#id>text</>); only the text matters here.
    static string[] Texts(Database db) =>
        db.Records.Select(r => System.Text.RegularExpressions.Regex.Replace(r.Text, @"<#\d+>(.*?)</>", "$1")).ToArray();

    // ---- entity comparison and id ----

    [Test]
    public void EntitiesCompareDirectly()
    {
        var db = Run(Things + @"
event e {
    create Thing $a {
        x := 1
    }
    create Thing $b {
        x := 2
    }
    pick Thing $y: ($y != $a)
    record('{$y.x}')
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "2" }));
    }

    [Test]
    public void IdIsAReferenceToItsOwnType()
    {
        // `id` read through a Thing is a Thing, so it goes where a Thing is expected.
        var db = Run(Things + @"
function big($t: Thing): bool {
    $t.x > 1
}
event e {
    create Thing $a {
        x := 2
    }
    if big($a.id) {
        record('big')
    }
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "big" }));
    }

    [Test]
    public void AnArgumentTakesTheSameConversionsAsSet()
    {
        // A number into a percentage parameter used to be a type error for a call, though `set` took it.
        var db = Run(Things + @"
function half($v: percentage): number {
    $v / 2
}
event e {
    record('{half(40)}')
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "20" }));
    }

    // ---- random_weighted ----

    [Test]
    public void RandomWeightedWithoutATotalSumsItsWeights()
    {
        var db = Run(Things + @"
event e {
    random_weighted {
        3 => record('a')
        1 => record('b')
    }
}
", out _, printed => Assert.That(printed, Does.Contain("random_weighted {")));
        var counts = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0 };
        for (int i = 0; i < 4000; i++)
            db.RunAction("e");
        foreach (var r in db.Records)
            counts[r.Text]++;
        Assert.That(counts["a"] + counts["b"], Is.EqualTo(4000), "every draw lands on a case");
        Assert.That(counts["a"] / 4000.0, Is.EqualTo(0.75).Within(0.03));
    }

    [Test]
    public void RandomWeightedWithoutATotalMatchesTheSameStoryWithItsSum()
    {
        // Same draws, same outcomes: the inferred total is exactly the explicit one.
        string Story(string total) => Things + $@"
event e {{
    random_weighted {total}{{
        3 => record('a')
        5 => record('b')
        2 => record('c')
    }}
}}
";
        var inferred = Run(Story(""), out _);
        var explicitTotal = Run(Story("10 "), out _);
        for (int i = 0; i < 200; i++)
        {
            inferred.RunAction("e");
            explicitTotal.RunAction("e");
        }

        Assert.That(Texts(inferred), Is.EqualTo(Texts(explicitTotal)));
    }

    [Test]
    public void AnyValueNeedsAnExplicitTotal()
    {
        StoryParser.Parse(Things + @"
event e {
    random_weighted {
        3 => record('a')
        _ => record('b')
    }
}
", out var errors);
        Assert.That(errors.Select(e => e.Code), Has.Member(StoryParser.ErrorCode.MatchNullWeight));
    }

    // ---- chance ----

    [Test]
    public void ChanceHitsAboutAsOftenAsItSays()
    {
        var db = Run(Things + @"
event e {
    if chance(30%) {
        record('hit')
    }
}
", out _);
        for (int i = 0; i < 5000; i++)
            db.RunAction("e");
        Assert.That(db.Records.Count / 5000.0, Is.EqualTo(0.3).Within(0.03));
    }

    [Test]
    public void AMissedChanceStatementDoesNotStopTheRule()
    {
        var db = Run(Things + @"
event e {
    chance(0%) {
        record('never')
    }
    chance(100%) {
        record('always')
    }
    record('after')
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "always", "after" }));
    }

    [Test]
    public void ChanceIsRejectedInsideAQuery()
    {
        StoryParser.Parse(Things + @"
event e {
    pick Thing $t: (alive and chance(50%))
}
", out var errors);
        Assert.That(errors.Select(e => e.Code), Has.Member(StoryParser.ErrorCode.InvalidArgument));
    }

    // ---- pick ... else ----

    [Test]
    public void PickElseRunsItsBlockThenStops()
    {
        var db = Run(Things + @"
event e {
    pick Thing $t: (x = 99) else {
        record('nobody')
    }
    record('found {$t.x}')
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "nobody" }));
    }

    [Test]
    public void PickElseIsSkippedWhenThePickSucceeds()
    {
        var db = Run(Things + @"
event e {
    create Thing $a {
        x := 7
    }
    pick Thing $t: (x = 7) else {
        record('nobody')
    }
    record('found {$t.x}')
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "found 7" }));
    }

    [Test]
    public void PickElseCannotSeeThePickedVariable()
    {
        StoryParser.Parse(Things + @"
event e {
    pick Thing $t: (x = 99) else {
        record('{$t.x}')
    }
}
", out var errors);
        Assert.That(errors.Select(e => e.Code), Has.Member(StoryParser.ErrorCode.VariableNotDeclared));
    }

    [TestCase("create Thing $t: ('x') else {")]
    [TestCase("each Thing $t: (alive) else {")]
    [TestCase("record('x') else {")]
    [TestCase("helper() else {")]
    public void OnlyAPickTakesAnElse(string line)
    {
        StoryParser.Parse(Things + $@"
function helper() {{
    record('h')
}}
event e {{
    {line}
        record('lost')
    }}
}}
", out var errors);
        Assert.That(errors.Select(e => e.Code), Has.Member(StoryParser.ErrorCode.InvalidArgument));
    }

    [Test]
    public void AnIfAroundAPickKeepsItsOwnElse()
    {
        var db = Run(Things + @"
event e {
    if (pick Thing $t: (x = 99)) {
        record('found')
    } else {
        record('not found')
    }
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "not found" }));
    }

    // ---- aggregates ----

    private const string Census = Things + @"
@start
event setup {
    create Place $big {
        size := 1
    }
    create Place $small {
        size := 2
    }
    create Thing $a {
        x := 1
        p := 10%
        f := 0.5
        alive := true
        place := $big
    }
    create Thing $b {
        x := 4
        p := 60%
        f := 1.5
        alive := true
        place := $big
    }
    create Thing $c {
        x := 10
        p := 80%
        alive := false
        place := $small
    }
}
";

    [Test]
    public void AggregatesFoldAQuery()
    {
        var db = Run(Census + @"
event e {
    record('count {count Thing $t: (alive)}')
    record('all {count Thing $t: ()}')
    record('sum {sum Thing $t: (alive, $t.x)}')
    record('avg {avg Thing $t: (alive, $t.x)}')
    record('min {min Thing $t: ($t.x)}')
    record('max {max Thing $t: ($t.x)}')
    record('avgp {avg Thing $t: (alive, $t.p)}')
    record('sump {sum Thing $t: ($t.p)}')
    record('sumf {sum Thing $t: (alive, $t.f)}')
    record('none {avg Thing $t: (x = 42, $t.x)}')
    record('bare {count Thing $t}')
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[]
        {
            "count 2", "all 3", "sum 5", "avg 2.5", "min 1", "max 10", "avgp 35%", "sump 150", "sumf 2", "none 0", "bare 3",
        }));
    }

    [Test]
    public void AnAggregateInsideAPickSeesThePickedEntity()
    {
        // The inner query reads the outer variable, per candidate: the place with two things is picked.
        var db = Run(Census + @"
event e {
    pick Place $pl: (count Thing $t: (place = $pl) = 2)
    record('size {$pl.size}')
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "size 1" }));
    }

    [Test]
    public void AggregatesNestAndAgreeWithAScan()
    {
        // An aggregate inside an aggregate's predicate, over another type, reading the outer variable, with
        // `or`s on both levels so each builds a union narrowing on the scratch stack while the other's scan
        // is still walking its own. Checked against a plain LINQ count below.
        var db = Run(Census + @"
event grow {
    create Thing $n {
        x := random(1, 20)
        alive := true
    }
    pick Place $pl: ()
    set $n.place = $pl
}
event e {
    pick Place $first: (size = 1)
    record('{count Place $pl: (size = 2 or sum Thing $t: (place = $pl or place = $first, $t.x) > 250)}')
    each Place $pl: () {
        record('{$pl.size}={count Thing $t: (place = $pl and alive or place = $first)}')
    }
}
", out _);
        for (int i = 0; i < 40; i++)
            db.RunAction("grow");
        db.RunAction("e");

        var place = db.GetPropertyId("Thing", "place");
        var x = db.GetPropertyId("Thing", "x");
        var alive = db.GetPropertyId("Thing", "alive");
        var size = db.GetPropertyId("Place", "size");
        var things = db.Entities.Where(en => db.GetEntityType(en.Type).Name == "Thing").ToList();
        var places = db.Entities.Where(en => db.GetEntityType(en.Type).Name == "Place").ToList();
        var first = places.Single(pl => pl.GetProperty(size).IntValue == 1).Id;
        bool At(Entity t, EntityId pl) => t.GetProperty(place).Id.Equals(pl);
        int expected = places.Count(pl => pl.GetProperty(size).IntValue == 2 ||
            things.Where(t => At(t, pl.Id) || At(t, first)).Sum(t => t.GetProperty(x).IntValue) > 250);
        var perPlace = places.Select(pl =>
            $"{pl.GetProperty(size).IntValue}={things.Count(t => At(t, pl.Id) && t.GetProperty(alive).BoolValue || At(t, first))}");

        Assert.That(Texts(db), Is.EqualTo(new[] { expected.ToString() }.Concat(perPlace)));
    }

    [Test]
    public void AnAggregateDrawsNoRandomNumbers()
    {
        string Story(string extra) => Census + $@"
event e {{
    {extra}
    record('{{random(0, 1000)}}')
}}
";
        var with = Run(Story("var $n: avg Thing $t: (alive, $t.x)"), out _);
        var without = Run(Story("var $n: 0"), out _);
        for (int i = 0; i < 20; i++)
        {
            with.RunAction("e");
            without.RunAction("e");
        }

        Assert.That(Texts(with), Is.EqualTo(Texts(without)));
    }

    [TestCase("sum Thing $t: (alive)", Description = "no value")]
    [TestCase("sum Thing $t: (alive, $t.place)", Description = "not a number")]
    [TestCase("sum(1)", Description = "no query")]
    public void AggregateMistakesAreErrors(string expr)
    {
        StoryParser.Parse(Things + $@"
event e {{
    var $n: {expr}
}}
", out var errors);
        Assert.That(errors.Count(e => e.Severity == StoryParser.Severity.Error), Is.GreaterThan(0));
    }

    [Test]
    public void TheAggregateVariableEndsWithTheCall()
    {
        StoryParser.Parse(Census + @"
event e {
    var $n: count Thing $t: (alive)
    record('{$t.x}')
}
", out var errors);
        Assert.That(errors.Select(e => e.Code), Has.Member(StoryParser.ErrorCode.VariableNotDeclared));
    }

    [Test]
    public void CountStillCountsACollection()
    {
        var db = Run(@"
entity Bag {
    prop items: [Bag]
}
event e {
    create Bag $a
    create Bag $b
    add($a.items, $b)
    record('{count($a.items)}')
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "1" }));
    }

    // ---- comments ----

    [Test]
    public void BlockCommentsAreIgnoredAndEndTheLineTheyCross()
    {
        var db = Run(Things + @"
/* a comment
   over several lines */
event e {
    record('a') /* after */
    /* before */ record('b')
    record(/* inside */ 'c')
    /* spanning
    */ record('d')
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "a", "b", "c", "d" }));
    }

    [Test]
    public void AnUnterminatedBlockCommentIsALexerError()
    {
        var tokens = MoiraiTokenizer.Tokenize("event e {\n}\n/* never closed\n");
        Assert.That(tokens.Errors, Has.Count.EqualTo(1));
        Assert.That(tokens.FullTokens.Count(t => t.Kind == MoiraiTokenKind.Comment), Is.EqualTo(1));
    }

    [Test]
    public void ABlockCommentIsOneCommentTokenPerLine()
    {
        var kinds = MoiraiTokenizer.Tokenize("/* a\nb */").FullTokens.Select(t => (t.Kind, t.ToStringValue()));
        Assert.That(kinds, Is.EqualTo(new[]
        {
            (MoiraiTokenKind.Comment, "/* a"), (MoiraiTokenKind.LineBreak, "\n"), (MoiraiTokenKind.Comment, "b */"),
        }));
    }

    [Test]
    public void ASlashStarInsideAStringIsText()
    {
        var db = Run(Things + @"
event e {
    record('a /* b */ c')
}
", out _);
        db.RunAction("e");
        Assert.That(Texts(db), Is.EqualTo(new[] { "a /* b */ c" }));
    }
}
