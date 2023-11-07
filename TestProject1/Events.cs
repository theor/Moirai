using Moirai;
using Moirai.Core;

namespace TestProject1;

public class Events
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
    public static (int, int)[] MakePairs => new[]
    {
        (2,10),
        (1,5),
        (10,5),
    };
}