using Moirai.Core;
using ExecutionContext = Moirai.Core.ExecutionContext;

public struct AssertInstr : IValueCall
{
    public enum AssertMode
    {
        True,
        Eq,
    }

    public readonly AssertMode Mode;
    public readonly IValue Value;
    public readonly IValue? Right;
    public readonly string Message;
    public AssertInstr(IValue value, string message) : this()
    {
        Mode = AssertMode.True;
        Value = value;
        Message = message;
    }
    public AssertInstr(IValue left, IValue? right, string message) : this(left, message)
    {
        Mode = AssertMode.Eq;
        Right = right;
    }
    public PropertyValue Compute(ExecutionContext ctx)
    {
        switch (Mode)
        {

            case AssertMode.True:
                ctx.Assert(Value.Compute(ctx).BoolValue, Message);
                break;
            case AssertMode.Eq:
                PropertyValue left = Value.Compute(ctx);
                PropertyValue right = Right.Compute(ctx);
                ctx.Assert(left == right,
                    $"{Message}, actual values:\n     left: {ctx.Database.Printer.Print(left)}\n    right: {ctx.Database.Printer.Print(right)}");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        return true;
    }

    public (string where, string? joins) ToSql(ExecutionContext ctx)
    {
        throw new NotImplementedException();
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }
    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        switch (Mode)
        {
            case AssertMode.True:
                yield return Value;
                // yield return new Literal(Message);
                break;
            case AssertMode.Eq:
                yield return Value;
                yield return Right!;
                // yield return new Literal(Message);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
