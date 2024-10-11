using Moirai.Core;

using System.Diagnostics;

public struct DebugPrint : IValueCall
{
    public IValue[] Args;

    public DebugPrint(IEnumerable<IValue?> args)
    {
        Args = args.Where(x => x!=null).ToArray()!;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        string msg = "[DBG] " + string.Join(", ",
            Args.Select(x => $"{ctx.Database.Printer.Print(x)}: {ctx.Database.Printer.Print(x.Compute(ctx))}"));
        if (Debugger.IsAttached)
            Debug.WriteLine(msg);
        else
            Console.WriteLine(msg);
        return true;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        return Args!;
    }
}
