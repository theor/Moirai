using Moirai;
using Moirai.Core;

namespace TestProject1;

public class FilterEvents
{

    [Test]
    public void Filter_Frequency(
        [ValueSource(nameof(MakePairs))](int eventsPer, int interval) p,
        [Values(32ul,48932ul,12348932ul,42ul,12542ul, 142ul)] ulong seed
        )
    {
        RandomEvent e = new RandomEvent(p.eventsPer, p.interval);
        Console.WriteLine("Prob " + e.Probability);
        var pcg32 = new Pcg32(seed, 56);
        for (int i = 0; i < p.interval * 10000; i++)
        {
            var sample = e.Sample(pcg32);
            if(i < Math.Max(10, p.interval))
                Console.WriteLine(sample);
        }
        Console.WriteLine($"{e.Ratio} - happened {e.Occurences} over {e.Interval} - {e.Occurences/(e.Interval / (float)e.ExpectedInterval)}/{e.ExpectedInterval}");
        Assert.LessOrEqual(MathF.Abs(e.Ratio - e.Probability), 0.1f);
    }
    // @frequency(X, EveryXYear, Y): exactly X in every window of Y years, spread across the window.
    [TestCase(3, 10)]
    [TestCase(1, 90)]
    [TestCase(7, 4)]
    public void ExactlyXEveryYYears(int count, int years)
    {
        var ctx = new Database().Ctx;
        var filter = new FilterExactlyXEveryYYears(count, years, 0);
        const int windows = 400;
        var offsets = new List<int>();
        for (int w = 0; w < windows; w++)
        {
            int inWindow = 0;
            for (int y = 0; y < years; y++)
            {
                int n = filter.Compute(ctx, w * years + y);
                inWindow += n;
                offsets.AddRange(Enumerable.Repeat(y, n));
            }

            Assert.That(inWindow, Is.EqualTo(count), $"window {w}");
        }

        // Uniform over the window: the mean offset sits in the middle, not bunched at the end.
        Assert.That(offsets.Average(), Is.EqualTo((years - 1) / 2.0).Within(years * 0.05));
    }

    [Test]
    public void EveryYearIncludesYearZero()
    {
        var ctx = new Database().Ctx;
        var filter = new FilterExactlyXEveryYYears(2, 1, 0);
        Assert.That(filter.Compute(ctx, 0), Is.EqualTo(2));
        Assert.That(filter.Compute(ctx, 0), Is.EqualTo(0), "once per year");
        Assert.That(filter.Compute(ctx, 1), Is.EqualTo(2));
    }

    [Test]
    public void AYearNotAskedAboutDoesNotStrandItsOccurrences()
    {
        var ctx = new Database().Ctx;
        var filter = new FilterExactlyXEveryYYears(5, 10, 0);
        // Asked only at the start and the last year of the window: all five come due by then.
        int total = filter.Compute(ctx, 0) + filter.Compute(ctx, 9);
        Assert.That(total, Is.EqualTo(5));
    }

    public static (int, int)[] MakePairs => new[]
    {
        (2,10),
        (1,5),
        (10,5),
    };
}
