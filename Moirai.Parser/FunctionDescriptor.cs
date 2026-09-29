using Moirai.Parser.Ast;

namespace Moirai.Parser;

public class FunctionDescriptor : IFunctionDescriptor
{
    public delegate (IValueCall, PropertyValue.ValueType) ParseCallDelegate(FunctionParseContext context);

    public string FuncName { get; }
    public bool ExpectVariable { get; }
    public BuiltinDoc? Doc { get; }
    public string Documentation => Doc?.ToMarkdown() ?? "";
    private readonly ParseCallDelegate _parse;

    public FunctionDescriptor(string funcName, bool expectVariable, ParseCallDelegate parse, BuiltinDoc? doc = null)
    {
        FuncName = funcName;
        ExpectVariable = expectVariable;
        Doc = doc;
        _parse = parse;
    }

    public IValueCall Parse(AstVisitor parser, CallOrRawCall call, out PropertyValue.ValueType returnType)
    {
        (IValueCall, PropertyValue.ValueType) c = _parse(new FunctionParseContext(parser, call, null));
        returnType = c.Item2;
        if (c.Item1 != null)
            c.Item1.FunctionDescriptor = this;
        else if (call.Call != null)
            parser.AddError(StoryParser.ErrorCode.UnknownFunction, call.Span, "");
        else
            throw new InvalidOperationException(call.Span.ToStringValue());
        return c.Item1;
    }

    public string Print(StoryPrinter printer, IValueCall call)
    {
        // call (1,2)
        // call X $x: (12)
        // call X $x
        var args = call.GetArgs(printer);
        switch ((call.VariableIndex.HasValue, args.Count()))
        {
            case (false, 0):
                return ("not a call??");
            case (false, _):
                return $"{FuncName} ({string.Join(", ", call.GetArgs(printer).Select(a => printer.Print(a)))})";
            case (true, 0):
                return $"{FuncName} {printer.Print(call.VariableIndex!.Value.Item2)} ${call.VariableIndex.Value.Item1}";
            case (true, _):
                return
                    $"{FuncName} {printer.Print(call.VariableIndex!.Value.Item2)} ${call.VariableIndex.Value.Item1}: ({string.Join(", ", call.GetArgs(printer).Select(a => printer.Print(a)))})";
        }
    }
}
