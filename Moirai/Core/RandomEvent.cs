namespace Pcg.Core;


public struct RandomEvent
{
    public bool IsValid => ExpectedInterval != 0 && ExpectedOccurences != 0;
    public int ExpectedOccurences;
    public int ExpectedInterval;
    public float Probability => ExpectedInterval == 0 ? 0 : (ExpectedOccurences / (float)ExpectedInterval);

    public int Occurences = 0;
    public int Interval = 0;
    public float Ratio => Interval == 0 ? 0 : (Occurences / (float)Interval);
    public RandomEvent(int occurences, int expectedInterval)
    {
        ExpectedOccurences = occurences;
        ExpectedInterval = expectedInterval;
    }
    public int Sample(Pcg32 rnd)
    {
        int count = PoissonProbability(Probability, rnd);
        Interval++;
        Occurences += count;
        return count;
    }
        
    public readonly int PoissonProbability (double lambda, Pcg32 rnd)
    {
        double expLambda = Math.Exp (-lambda); //constant for terminating loop

        var randPoisson = -1;
        double prodUni = 1; //product of uniform variables
        do {
            double randUni = rnd.GenerateNextFloat(); //uniform variable
            prodUni *= randUni; //update product
            randPoisson++; // increase Poisson variable
        } while (prodUni > expLambda); 

        return randPoisson;
    }
    public void Set(int occurences, int expectedInterval)
    {
        ExpectedOccurences = occurences;
        ExpectedInterval = expectedInterval;
        Occurences = 0;
        Interval = 0;
    }
}