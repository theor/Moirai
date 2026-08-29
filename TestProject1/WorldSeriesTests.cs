using Moirai.Core;
using Moirai.Parser;

namespace TestProject1;

// WorldSeries reconstructs history that the engine never recorded, by replaying the changeset log.
// These tests pin the reconstruction against a story whose answers are known by hand, then check the
// shape holds on w.sg.
public class WorldSeriesTests
{
    private static Database Run(string story, int years, ulong seed = 42)
    {
        var db = StoryParser.Parse(story, out var errors);
        Assert.That(errors.Count(e => e.Severity == StoryParser.Severity.Error), Is.EqualTo(0),
            string.Join("\n", errors));
        db.SetSeed(seed);
        db.History = new();
        db.Init();
        if (years > 0)
            db.Ctx.PassYears(years, true);
        return db;
    }

    // One person born every year, dying at 3 — so from year 3 onward exactly 3 are alive, forever.
    // Every answer this story gives is known by hand, which is the point: the replay is checked against
    // arithmetic, not against itself.
    private const string Cohorts = """
        entity Person {
            prop alive: bool
            prop born: number
        }

        @start
        event start {
            create Time $t: 'time' {
                year := 0
            }
        }

        @frequency(1, EveryXYear, 1)
        event birth {
            create Person $p: 'p' {
                alive := true
                born := #Time.year
            }
            record('a birth')
        }

        @frequency(1, EveryXYear, 1)
        event reaper {
            each Person $p: ($p.alive and #Time.year - $p.born >= 3) {
                set $p.alive = false
            }
        }

        """;

    [Test]
    public void ABoolPropertyBecomesTheCountOfEntitiesHoldingItTrue()
    {
        var db = Run(Cohorts, 20);
        var series = WorldSeries.PropertyOverTime(db, db.GetEntityType("Person"), "alive");

        // The steady state is what the story guarantees; the first few years are the ramp up to it.
        Assert.That(series.Values[^1], Is.EqualTo(3).Within(0.001),
            "three people should be alive at any settled year");
        Assert.That(series.Values.Max(), Is.LessThanOrEqualTo(4), "the population should not run away");
        Assert.That(series.Years[^1], Is.EqualTo(db.Ctx.Year));
    }

    [Test]
    public void ANumericPropertyBecomesTheMeanOverEntitiesThatHaveOne()
    {
        var db = Run(Cohorts, 20);
        var series = WorldSeries.PropertyOverTime(db, db.GetEntityType("Person"), "born");

        // Every person ever born is in the mean (the dead are not removed, only their `alive` flips),
        // so the mean birth year climbs monotonically and lands inside the range of years seen.
        for (int i = 1; i < series.Values.Length; i++)
            Assert.That(series.Values[i], Is.GreaterThanOrEqualTo(series.Values[i - 1] - 0.001),
                $"mean birth year fell at index {i}");
        Assert.That(series.Values[^1], Is.InRange(0, db.Ctx.Year));
    }

    [Test]
    public void EntitiesOfTypeCountsCreationsCumulatively()
    {
        var db = Run(Cohorts, 20);
        var series = WorldSeries.EntitiesOfType(db, db.GetEntityType("Person"));
        Assert.That(series, Is.Not.Null);

        for (int i = 1; i < series!.Values.Length; i++)
            Assert.That(series.Values[i], Is.GreaterThanOrEqualTo(series.Values[i - 1]),
                "a cumulative count must never fall");
        Assert.That(series.Values[^1],
            Is.EqualTo(db.Entities.Count(e => e.Type == db.GetEntityType("Person").Id)),
            "the final sample must equal the number of Person entities that exist");
    }

    [Test]
    public void ATypeThatNeverAppearsHasNoSeries()
    {
        var db = Run(Cohorts + "\nentity Ghost {\n    prop spooky: bool\n}\n", 5);
        Assert.That(WorldSeries.EntitiesOfType(db, db.GetEntityType("Ghost")), Is.Null);
    }

    [Test]
    public void RatesAreAveragedPerYearSoDownsamplingDoesNotChangeTheScale()
    {
        var db = Run(Cohorts, 200);
        var full = WorldSeries.RecordsPerYear(db, 1000);
        var coarse = WorldSeries.RecordsPerYear(db, 10);

        // A rate bucket holds the mean of the years it covers, so both resolutions describe the same
        // quantity. Summing a rate would make the coarse series ten times taller.
        Assert.That(coarse.Values.Length, Is.LessThanOrEqualTo(10));
        Assert.That(coarse.Values.Max(), Is.LessThanOrEqualTo(full.Values.Max() + 0.001),
            "downsampling a rate must not inflate it");
    }

    [Test]
    public void LevelsKeepTheirLastSampleWhenDownsampled()
    {
        var db = Run(Cohorts, 200);
        var full = WorldSeries.EntitiesOfType(db, db.GetEntityType("Person"), 1000)!;
        var coarse = WorldSeries.EntitiesOfType(db, db.GetEntityType("Person"), 10)!;
        Assert.That(coarse.Values[^1], Is.EqualTo(full.Values[^1]).Within(0.001),
            "the final level must survive downsampling exactly");
    }

    [Test]
    public void SeriesAreSafeOnAWorldThatHasNotRunYet()
    {
        var db = Run(Cohorts, 0);
        Assert.That(WorldSeries.RecordsPerYear(db).Values, Is.Not.Empty);
        Assert.That(WorldSeries.ChangesPerYear(db).Values, Is.Not.Empty);
        Assert.That(WorldSeries.PropertyOverTime(db, db.GetEntityType("Person"), "alive").Values, Is.Not.Empty);
    }

    [Test]
    public void AnUnknownPropertyYieldsAnEmptySeriesRatherThanThrowing()
    {
        var db = Run(Cohorts, 5);
        Assert.That(WorldSeries.PropertyOverTime(db, db.GetEntityType("Person"), "nope"),
            Is.EqualTo(TimeSeries.Empty));
    }

    [Test]
    public void ChartableSkipsRefsStringsAndEnums()
    {
        var db = Run("""
            enum Job { Farmer, Smith }
            entity Person {
                prop alive: bool
                prop age: number
                prop wealth: percentage
                prop job: Job
                prop partner: Person
                prop epitaph: string
            }

            @start
            event start {
                create Time $t: 'time' {
                    year := 0
                }
            }

            """, 0);

        var names = WorldSeries.Chartable(db)
            .Where(c => c.Type.Name == "Person")
            .Select(c => c.Property.Name)
            .ToList();
        Assert.That(names, Is.EquivalentTo(new[] { "alive", "age", "wealth" }));
        Assert.That(WorldSeries.Chartable(db).Single(c => c.Property.Name == "alive").IsBool, Is.True);
        Assert.That(WorldSeries.Chartable(db).Single(c => c.Property.Name == "wealth").IsBool, Is.False);
    }

    // Stories rarely start at year zero (w.sg starts at 764). Indexing the series from zero padded every
    // chart with centuries of flat nothing before the world existed.
    [Test]
    public void SeriesStartAtTheYearTheWorldBegins()
    {
        var late = Cohorts.Replace("year := 0", "year := 764");
        var db = Run(late, 40);
        Assert.That(db.StartYear, Is.EqualTo(764));

        foreach (var series in new[]
                 {
                     WorldSeries.RecordsPerYear(db),
                     WorldSeries.ChangesPerYear(db),
                     WorldSeries.EntitiesOfType(db, db.GetEntityType("Person"))!,
                     WorldSeries.PropertyOverTime(db, db.GetEntityType("Person"), "alive"),
                 })
        {
            Assert.That(series.Years[0], Is.GreaterThanOrEqualTo(764), $"{series.Label} starts before the world");
            Assert.That(series.Years[^1], Is.EqualTo(db.Ctx.Year), $"{series.Label} ends short of the present");
        }
    }

    // Setup effects run inside Init() before Time exists, so their changesets carry year 0. They belong
    // at the start of the timeline, not off the end of it.
    [Test]
    public void SetupBeforeTimeExistsIsCountedInTheFirstYear()
    {
        var late = Cohorts.Replace("year := 0", "year := 764");
        var db = Run(late, 0);
        var changes = WorldSeries.ChangesPerYear(db);
        Assert.That(changes.Years[0], Is.EqualTo(764));
        Assert.That(changes.Values.Sum(), Is.GreaterThan(0), "the @start changeset was dropped");
    }

    [Test]
    public void StoryTypesSkipsTheEngineBuiltins()
    {
        var db = Run(Cohorts, 0);
        var names = WorldSeries.StoryTypes(db).Select(t => t.Name).ToList();
        Assert.That(names, Does.Contain("Person"));
        Assert.That(names, Does.Not.Contain("Time"), "Time is engine bookkeeping, not world state");
    }

    [Test]
    public void WsgPopulationIsPlausible()
    {
        var wsg = File.ReadAllText(FindWsg());
        var db = Run(wsg, 250);
        var alive = WorldSeries.PropertyOverTime(db, db.GetEntityType("Person"), "alive");
        Assert.That(alive.Values[^1], Is.GreaterThan(0), "everyone in w.sg died");
        Assert.That(alive.Values.Max(), Is.LessThan(db.Entities.Count()),
            "living people cannot outnumber all entities");

        var records = WorldSeries.RecordsPerYear(db);
        Assert.That(records.Values.Sum(), Is.GreaterThan(0));
    }

    private static string FindWsg()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "MoiraiCli", "w.sg");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate MoiraiCli/w.sg above " + AppContext.BaseDirectory);
    }
}
