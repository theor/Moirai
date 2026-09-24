using Moirai.Core;
using Moirai.Parser;

namespace TestProject1;

// related($a, $b, n): a shared ancestor at depths i and j with i + j <= n — the civil-law degree of kinship.
public class RelatedTests
{
    private const string People = """
        entity Person {
            prop parent1: Person
            prop parent2: Person
        }
        entity Rock {
            prop weight: number
        }

        """; // the parser wants a line break after the last definition

    private Database _db = null!;
    private PropertyId _p1, _p2;
    private readonly Dictionary<string, EntityId> _person = new();

    //            GG
    //            |
    //       G1 + G2
    //      /       \
    //  S1 + P1     P2 + S2        U: unrelated founder
    //   |     \       |
    //  C1     C3     C2
    [SetUp]
    public void BuildFamily()
    {
        _db = StoryParser.Parse(People, out var errors);
        Assert.That(errors.Where(e => e.Severity == StoryParser.Severity.Error), Is.Empty);
        _db.History = new();
        _db.Init();
        var type = _db.GetEntityType("Person");
        _p1 = type.GetPropertyId("parent1");
        _p2 = type.GetPropertyId("parent2");
        _person.Clear();

        void Person(string name, string? mother = null, string? father = null)
        {
            var id = _db.AllocateEntity(type.Id, name);
            _person[name] = id;
            if (mother != null) _db.SetProperty(id, _p1, _person[mother]);
            if (father != null) _db.SetProperty(id, _p2, _person[father]);
        }

        Person("GG");
        Person("G1", "GG");
        Person("G2");
        Person("P1", "G1", "G2");
        Person("P2", "G2", "G1"); // parents in the other slots: order must not matter
        Person("S1");
        Person("S2");
        Person("C1", "P1", "S1");
        Person("C3", "S1", "P1");
        Person("C2", "P2", "S2");
        Person("U");
    }


    private bool Kin(string a, string b, int degree) =>
        new global::Related(new Literal(_person[a]), new Literal(_person[b]), degree, _p1, _p2).Compute(_db.Ctx).BoolValue;

    [TestCase("P1", "G1", 1, TestName = "parent")]
    [TestCase("C1", "G1", 2, TestName = "grandparent")]
    [TestCase("P1", "P2", 2, TestName = "siblings")]
    [TestCase("C1", "C3", 2, TestName = "siblings, parents in swapped slots")]
    [TestCase("C1", "GG", 3, TestName = "great-grandparent")]
    [TestCase("C1", "P2", 3, TestName = "aunt")]
    [TestCase("C1", "C2", 4, TestName = "first cousins")]
    public void RelatedFromItsDegreeOnwards(string a, string b, int degree)
    {
        for (var n = 0; n <= global::Related.MaxDegree; n++)
        {
            Assert.That(Kin(a, b, n), Is.EqualTo(n >= degree), $"{a}–{b} at degree {n}");
            Assert.That(Kin(b, a, n), Is.EqualTo(n >= degree), $"{b}–{a} at degree {n}");
        }
    }

    [TestCase("C1", "U", TestName = "a stranger")]
    [TestCase("S1", "S2", TestName = "two founders with no parents")]
    [TestCase("C1", "S2", TestName = "an aunt by marriage")]
    public void NeverRelated(string a, string b) =>
        Assert.That(Kin(a, b, global::Related.MaxDegree), Is.False);

    [Test]
    public void AnEntityIsRelatedToItself() => Assert.That(Kin("U", "U", 0), Is.True);

    [Test]
    public void AWriteMakesItForgetTheSideItRemembered()
    {
        // One node, one fixed side — as inside a pick — asked twice with a write in between.
        var node = new global::Related(new Literal(_person["C1"]), new Literal(_person["U"]), 2, _p1, _p2);
        Assert.That(node.Compute(_db.Ctx).BoolValue, Is.False);

        _db.SetProperty(_person["U"], _p1, _person["P1"]); // U turns out to be C1's half sibling
        Assert.That(node.Compute(_db.Ctx).BoolValue, Is.True);

        _db.SetProperty(_person["C1"], _p1, default(EntityId)); // and C1 loses the parent they shared
        _db.SetProperty(_person["C1"], _p2, default(EntityId));
        Assert.That(node.Compute(_db.Ctx).BoolValue, Is.False);
    }

    [Test]
    public void APickSkipsKin()
    {
        var db = StoryParser.Parse(People + """

            @start
            event start {
                create Time $t: 'time' {
                    year := 0
                }
                create Person $a: 'a'
                create Person $b: 'b'
                create Person $c: 'c' {
                    parent1 := $a
                }
                each Person $p: (not(related($p, $a, 1))) {
                    record('{$p.name} is no kin of a')
                }
            }
            """, out var errors);
        Assert.That(errors.Where(e => e.Severity == StoryParser.Severity.Error), Is.Empty,
            string.Join("\n", errors));
        db.History = new();
        db.Init();

        Assert.That(db.Records.Select(r => r.Text), Is.EqualTo(new[] { "<#3>b</> is no kin of a" }));
    }

    [TestCase("related($p, $p, 7)", StoryParser.ErrorCode.InvalidArgument, TestName = "degree too deep")]
    [TestCase("related($p, $p, $p)", StoryParser.ErrorCode.InvalidArgument, TestName = "degree not a literal")]
    [TestCase("related($r, $r, 2)", StoryParser.ErrorCode.UnknownProperty, TestName = "a type with no parents")]
    public void BadCallsAreParseErrors(string call, StoryParser.ErrorCode expected)
    {
        StoryParser.Parse(People + $$"""

            event probe {
                pick Person $p
                pick Rock $r
                if {{call}} {
                    record('x')
                }
            }
            """, out var errors);
        Assert.That(errors.Select(e => e.Code), Does.Contain(expected), string.Join("\n", errors));
    }
}
