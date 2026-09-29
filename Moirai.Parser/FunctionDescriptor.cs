using Moirai.Parser.Ast;

namespace Moirai.Parser;

public class FunctionDescriptor : IFunctionDescriptor
{
    public delegate (IValueCall, PropertyValue.ValueType) ParseCallDelegate(FunctionParseContext context);

    /// A handler for a checked built-in: it receives arguments already bound to one of its forms, and the
    /// return type comes from that form.
    public delegate IValueCall? BoundCallDelegate(BoundCall call);

    public string FuncName { get; }
    public bool ExpectVariable { get; }

    /// How the built-in can be written. Empty for the few whose syntax is its own (`call`, `schedule`,
    /// `chance`), which keep hand-written signatures in their <see cref="BuiltinDoc"/>.
    public FunctionForm[] Forms { get; } = [];

    /// True when every form is a <see cref="CallForm"/> the parser checks calls against, rather than a
    /// description of what a hand-written handler accepts.
    public bool IsChecked => _bound != null;

    public BuiltinDoc? Doc { get; }
    public string Documentation => Doc?.ToMarkdown() ?? "";
    private readonly ParseCallDelegate? _parse;
    private readonly BoundCallDelegate? _bound;

    /// A built-in with syntax of its own and a hand-written signature.
    public FunctionDescriptor(string funcName, bool expectVariable, ParseCallDelegate parse, BuiltinDoc? doc = null)
    {
        FuncName = funcName;
        ExpectVariable = expectVariable;
        Doc = doc;
        _parse = parse;
    }

    /// A built-in whose forms describe what its hand-written handler accepts: the binding forms, whose
    /// parsing depends on scoping the binder does not model.
    public FunctionDescriptor(string funcName, bool expectVariable, FunctionForm[] forms, ParseCallDelegate parse,
        FunctionDoc doc)
    {
        FuncName = funcName;
        ExpectVariable = expectVariable;
        Forms = forms;
        Doc = MakeDoc(funcName, forms, doc);
        _parse = parse;
    }

    /// A plain-call built-in the parser checks against its forms before the handler runs.
    public FunctionDescriptor(string funcName, CallForm[] forms, BoundCallDelegate parse, FunctionDoc doc)
    {
        FuncName = funcName;
        Forms = forms;
        Doc = MakeDoc(funcName, forms, doc);
        _bound = parse;
    }

    static BuiltinDoc MakeDoc(string name, FunctionForm[] forms, FunctionDoc doc) =>
        new(doc.Category, forms.Select(f => f.Signature(name)).ToArray(), doc.Summary, doc.Example);

    public IValueCall Parse(AstVisitor parser, CallOrRawCall call, out PropertyValue.ValueType returnType)
    {
        var ctx = new FunctionParseContext(parser, call, null);
        if (_bound != null)
        {
            returnType = default;
            // A call that cannot be bound has been reported, argument by argument; there is nothing to add.
            if (!FunctionBinder.TryBind(FuncName, Forms.Cast<CallForm>().ToArray(), ctx, out var bound))
                return null!;
            returnType = bound.ReturnType;
            var checkedCall = _bound(bound);
            if (checkedCall != null)
                checkedCall.FunctionDescriptor = this;
            return checkedCall!;
        }

        (IValueCall, PropertyValue.ValueType) c = _parse!(ctx);
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
