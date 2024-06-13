using Moirai.Core;

public struct MathUnary : IValueCall
{
    public enum UnaryFunction
    {
        Not,
        Floor,
        Ceiling,
        Round,
        Clamp01,
    }


    public readonly UnaryFunction Function;
    public readonly IValue Arg;

    public MathUnary(UnaryFunction function, IValue arg)
    {
        Arg = arg;
        Function = function;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        switch (Function)
        {
            case UnaryFunction.Not:
                return !Arg.Compute(ctx).BoolValue;
            case UnaryFunction.Floor:
                return (int)MathF.Floor(Arg.Compute(ctx).FloatValue);
            case UnaryFunction.Ceiling:
                return (int)MathF.Ceiling(Arg.Compute(ctx).FloatValue);
            case UnaryFunction.Round:
                return (int)MathF.Round(Arg.Compute(ctx).FloatValue);
            case UnaryFunction.Clamp01:
                return Math.Clamp(Arg.Compute(ctx).FloatValue, 0f, 1f);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public (string where, string? joins) ToSql(ExecuteContext ctx)
    {
        throw new NotImplementedException();
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return Arg;
    }
}
