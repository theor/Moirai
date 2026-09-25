using Moirai.Api;
using Moirai.Parser;

namespace TestProject1;

/// Role attributes tell the engine and the viewer what a property means -- a parent, a birth year, a
/// death flag, the bounds of an age -- instead of it being guessed from w.sg's names. What a story does
/// not declare still comes from those names, so an unannotated story reads exactly as before.
public class RoleAttributeTests
{
    // Kin under names the convention would never find, and death as a `dead` flag rather than `alive`.
    private const string Clan = @"
@parents(mother, father)
@partner(spouse)
@born(born_in)
@died(died_in)
@dead(fallen)
@population
entity Kin {
    prop mother: Kin
    prop father: Kin
    prop spouse: Kin
    prop born_in: number
    prop died_in: number
    prop fallen: bool
    function cousins($other: Kin): bool {
        related($self, $other, 4)
    }
}
@period(from, to)
entity Age {
    prop from: number
    prop to: number
}
@start
event begin {
    create Time $t: 'time' {
        year := 100
    }
    create Age $a: ('The First Age') {
        from := 100
    }
    create Kin $m: ('Mara') {
        born_in := 80
    }
    create Kin $f: ('Fen') {
        born_in := 78
        spouse := $m
    }
    create Kin $c: ('Cai') {
        mother := $m
        father := $f
        born_in := 100
    }
    set $f.fallen = true
    set $f.died_in = 100
}
";

    private static Database Parse(string story, out List<StoryParser.Error> errors) =>
        StoryParser.Parse(story, out errors);

    private static EntityType Type(Database db, string name) => db.Types.First(t => t.Name == name);

    [Test]
    public void DeclaredRolesNameTheirProperties()
    {
        var db = Parse(Clan, out var errors);
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors.Select(e => e.Message)));

        var kin = Type(db, "Kin");
        string Name(EntityRole r) => db.GetPropertyName(kin.Role(r));
        Assert.That(Name(EntityRole.Parent1), Is.EqualTo("mother"));
        Assert.That(Name(EntityRole.Parent2), Is.EqualTo("father"));
        Assert.That(Name(EntityRole.Partner), Is.EqualTo("spouse"));
        Assert.That(Name(EntityRole.Birth), Is.EqualTo("born_in"));
        Assert.That(Name(EntityRole.Death), Is.EqualTo("died_in"));
        Assert.That(Name(EntityRole.Dead), Is.EqualTo("fallen"));
        Assert.That(kin.Role(EntityRole.Alive).IsValid, Is.False);
        Assert.That(kin.IsPopulation, Is.True);
        Assert.That(Type(db, "Age").IsPeriod, Is.True);
    }

    [Test]
    public void RelatedInsideAnEntityMethodSeesTheDeclaredParents()
    {
        // Roles are resolved before any body is parsed, a type's own methods included.
        Parse(Clan, out var errors);
        Assert.That(errors.Select(e => e.Message), Has.None.Contains("related()"));
    }

    [Test]
    public void TheViewerReadsTheFamilyAndTheDeadThroughTheRoles()
    {
        var s = new WorldSession(Clan);
        var cai = s.Database.Entities.Single(e => s.GetBiography(e.Id.Id).Name == "Cai").Id.Id;

        Assert.That(s.GetBiography(cai).HasFamily, Is.True);
        var tree = s.GetFamilyTree(cai, 3);
        var fen = tree.Single(n => n.Name == "Fen");
        var mara = tree.Single(n => n.Name == "Mara");
        Assert.That(fen.Dead, Is.True, "@dead(fallen)");
        Assert.That(fen.Died, Is.EqualTo(100));
        Assert.That(fen.Born, Is.EqualTo(78));
        Assert.That(fen.Partner, Is.EqualTo(mara.Id));
        Assert.That(tree.Single(n => n.Name == "Cai").P1, Is.EqualTo(mara.Id));
    }

    [Test]
    public void TheChronicleCountsTheLivingAndDrawsTheDeclaredAges()
    {
        var s = new WorldSession(Clan);
        s.PassYears(3);

        var c = s.GetChronicle(5);
        Assert.That(c.Population.Label, Is.EqualTo("Kin alive"));
        // Three were made; Fen fell. Unset means alive -- nobody is born dead.
        Assert.That(c.Population.Values[^1], Is.EqualTo(2));
        Assert.That(c.Eras.Select(e => e.Name), Is.EqualTo(new[] { "The First Age" }));
        Assert.That(s.Database.Printer.ExportChronicle(), Does.Contain("The First Age"));
    }

    [Test]
    public void DeclaredRolesSurviveAPrintAndReparse()
    {
        var db = Parse(Clan, out _);
        var printed = db.Printer.Print();
        Assert.That(printed, Does.Contain("@parents(mother, father)"));
        Assert.That(printed, Does.Contain("@dead(fallen)"));
        Assert.That(printed, Does.Contain("@population"));
        Assert.That(printed, Does.Contain("@period(from, to)"));

        var again = Parse(printed, out _);
        var kin = Type(again, "Kin");
        Assert.That(again.GetPropertyName(kin.Role(EntityRole.Parent1)), Is.EqualTo("mother"));
        Assert.That(again.GetPropertyName(kin.Role(EntityRole.Dead)), Is.EqualTo("fallen"));
        Assert.That(kin.IsPopulation, Is.True);
    }

    [Test]
    public void AnUnannotatedStoryGetsItsRolesFromTheConventionalNamesAndPrintsNoneOfThem()
    {
        var db = Parse(@"
entity Person {
    prop parent1: Person
    prop parent2: Person
    prop partner: Person
    prop birthdate: number
    prop deathdate: number
    prop alive: bool
}
entity Era {
    prop start_year: number
    prop end_year: number
}
entity Holding {
    prop owner: Person
    prop start_year: number
    prop end_year: number
}
", out var errors);
        Assert.That(errors, Is.Empty);

        var person = Type(db, "Person");
        Assert.That(person.HasParents, Is.True);
        foreach (var r in new[] { EntityRole.Partner, EntityRole.Birth, EntityRole.Death, EntityRole.Alive })
            Assert.That(person.Role(r).IsValid, Is.True, r.ToString());
        Assert.That(person.IsDeclared(EntityRole.Parent1), Is.False);
        Assert.That(Type(db, "Era").IsPeriod, Is.True);
        Assert.That(Type(db, "Holding").IsPeriod, Is.False, "a type with references is a spell of something, not an age");
        Assert.That(db.Printer.Print(), Does.Not.Contain("@parents"));
    }

    [Test]
    public void ADeclaredDeadFlagSuppressesTheAliveDefault()
    {
        var db = Parse("@dead(dead)\nentity P {\n    prop alive: bool\n    prop dead: bool\n}\n", out var errors);
        Assert.That(errors, Is.Empty);
        Assert.That(Type(db, "P").Role(EntityRole.Alive).IsValid, Is.False);
    }

    [TestCase("@parents(mother)", StoryParser.ErrorCode.MissingArgument)]
    [TestCase("@parents(mother, nobody)", StoryParser.ErrorCode.UnknownProperty)]
    [TestCase("@parents(mother, age)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@parents(mother, pet)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@born(flag)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@dead(age)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@born('age')", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@alive(flag)\n@dead(flag)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@population(flag)", StoryParser.ErrorCode.InvalidArgument)]
    [TestCase("@lineage(mother)", StoryParser.ErrorCode.UnknownAttribute)]
    public void AMistakenRoleIsAnErrorRatherThanASilentMiss(string attributes, StoryParser.ErrorCode code)
    {
        var story = "entity Pet {}\n" + attributes +
                    "\nentity P {\n    prop mother: P\n    prop pet: Pet\n    prop age: number\n    prop flag: bool\n}\n";
        Parse(story, out var errors);
        Assert.That(errors.Select(e => e.Code), Has.Member(code), () => string.Join("\n", errors.Select(e => e.Message)));
    }

    [Test]
    public void OnlyOneTypeCanBeThePopulation()
    {
        Parse("@population\nentity A {}\n@population\nentity B {}\n", out var errors);
        Assert.That(errors.Select(e => e.Message), Has.Some.Contains("only one type"));
    }
}
