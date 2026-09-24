using Moirai.Core;

namespace TestProject1;

// `and` stops at a false left side and `or` at a true one. A story orders a pick's clauses expecting the
// cheap ones to filter first; evaluating every side made each candidate pay for the most expensive clause.
public class ShortCircuitTests
{
    private sealed class Counting(bool value) : IValue
    {
        public int Calls;

        public PropertyValue Compute(ExecuteContext ctx)
        {
            Calls++;
            return value;
        }
    }

    private static ExecuteContext Ctx() => new Database().Ctx;

    [TestCase(BinaryOperator.Operator.And, false, false)]
    [TestCase(BinaryOperator.Operator.Or, true, true)]
    public void TheRightSideIsSkippedWhenTheLeftSettlesIt(BinaryOperator.Operator op, bool left, bool expected)
    {
        var right = new Counting(!left);
        var result = new BinaryOperator(op, new Counting(left), right).Compute(Ctx());

        Assert.That(result.BoolValue, Is.EqualTo(expected));
        Assert.That(right.Calls, Is.Zero);
    }

    [TestCase(BinaryOperator.Operator.And, true, false, false)]
    [TestCase(BinaryOperator.Operator.And, true, true, true)]
    [TestCase(BinaryOperator.Operator.Or, false, false, false)]
    [TestCase(BinaryOperator.Operator.Or, false, true, true)]
    public void TheRightSideDecidesOtherwise(BinaryOperator.Operator op, bool left, bool rightValue, bool expected)
    {
        var right = new Counting(rightValue);
        var result = new BinaryOperator(op, new Counting(left), right).Compute(Ctx());

        Assert.That(result.BoolValue, Is.EqualTo(expected));
        Assert.That(right.Calls, Is.EqualTo(1));
    }
}
