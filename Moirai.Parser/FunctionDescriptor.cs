using Moirai.Parser.Ast;

namespace Moirai.Parser;

public class FunctionDescriptor : IFunctionDescriptor
{
    /// The handler: it receives arguments already bound to one of the built-in's forms, and the return type
    /// comes from that form.
    public delegate IValueCall? BoundCallDelegate(BoundCall call);

    public string FuncName { get; }
    public bool ExpectVariable { get; }

    /// How the built-in can be written. The parser checks every call against them before the handler runs,
    /// and the reference's signatures are generated from them.
    public FunctionForm[] Forms { get; }

    public BuiltinDoc Doc { get; }
    public string Documentation => Doc.ToMarkdown();
    private readonly BoundCallDelegate _bound;

    /// <paramref name="prepare"/> runs after the arguments and before any block is parsed, for a handler
    /// that must claim something first (schedule reserves its stream key, so a schedule nested in its body is
    /// numbered after it).
    public FunctionDescriptor(string funcName, FunctionForm[] forms, BoundCallDelegate parse, FunctionDoc doc,
        Func<FunctionParseContext, object?>? prepare = null)
    {
        FuncName = funcName;
        ExpectVariable = forms.Any(f => f is BindingForm);
        Forms = forms;
        Doc = new BuiltinDoc(doc.Category, forms.Select(f => f.Signature(funcName)).ToArray(), doc.Summary, doc.Example);
        _bound = parse;
        Prepare = prepare;
    }

    public Func<FunctionParseContext, object?>? Prepare { get; }

    public IValueCall Parse(AstVisitor parser, CallOrRawCall call, out PropertyValue.ValueType returnType)
    {
        var ctx = new FunctionParseContext(parser, call, null);
        returnType = default;
        // A call that cannot be bound has been reported, argument by argument; there is nothing to add.
        if (!FunctionBinder.TryBind(this, ctx, out var bound))
            return null!;
        returnType = bound.ReturnType;
        var checkedCall = _bound(bound);
        if (checkedCall != null)
            checkedCall.FunctionDescriptor = this;
        return checkedCall!;
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
