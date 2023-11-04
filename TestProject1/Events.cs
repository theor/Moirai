using Pcg;

namespace TestProject1;

public class Events
{
    struct Event
    {
        public int ExpectedOccurences { get; }
        public int ExpectedInterval { get; }
        public float Probability => ExpectedOccurences / (float)ExpectedInterval;

        public int Occurences = 0;
        public int Interval = 0;
        public float Ratio => Interval == 0 ? 0 : (Occurences / (float)Interval);
        public Event(int occurences, int expectedInterval)
        {
            ExpectedOccurences = occurences;
            ExpectedInterval = expectedInterval;
        }
        public bool Sample(Pcg32 rnd)
        {
            float r = rnd.GenerateNext(100000) / 100000f;
            Interval++;
            if (r < Probability)
            {
                Occurences++;
                return true;
            }
            return false;
        }
    }
    [Test]
    [TestCase(32ul)]
    [TestCase(48932ul)]
    [TestCase(12348932ul)]
    [TestCase(42ul)]
    [TestCase(12542ul)]
    [TestCase(142ul)]
    public void Proba(ulong seed)
    {
        var interval = 15;
        Event e = new Event(1, interval);
        Console.WriteLine("Prob " + e.Probability);
        var pcg32 = new Pcg32(seed, 56);
        for (int i = 0; i < interval * 10000; i++)
        {
            var sample = e.Sample(pcg32);
            // Console.WriteLine(sample);
        }
        Console.WriteLine($"{e.Ratio} - happened {e.Occurences} over {e.Interval} - {e.Occurences/(e.Interval / (float)e.ExpectedInterval)}/{e.ExpectedInterval}");
        Assert.LessOrEqual(MathF.Abs(e.Ratio - e.Probability), 0.1f);
    }
    [Test]
    [TestCase(32ul)]
    [TestCase(48932ul)]
    [TestCase(12348932ul)]
    [TestCase(42ul)]
    [TestCase(12542ul)]
    [TestCase(142ul)]
    public void Proba2(ulong seed)
    {
        var interval = 200;
        Event e = new Event(1, interval);
        Console.WriteLine("Prob " + e.Probability);
        var pcg32 = new Pcg32(seed, 56);
        for (int i = 0; i < interval * 10000; i++)
        {
            var sample = e.Sample(pcg32);
            // Console.WriteLine(sample);
        }
        Console.WriteLine($"{e.Ratio} - happened {e.Occurences} over {e.Interval} - {e.Occurences/(e.Interval / (float)e.ExpectedInterval)}/{e.ExpectedInterval}");
        Assert.LessOrEqual(MathF.Abs(e.Ratio - e.Probability), 0.1f);
    }
}