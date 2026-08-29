using Superpower.Model;

namespace Moirai.Parser.Ast;

/// The two call-site shapes `FunctionParseContext` wraps: the parenthesized `call` form and the
/// bare/paren-less `raw_call` form. Replaces the old `is MoiraiParser.CallContext c` /
/// `is MoiraiParser.Raw_callContext r` branching that ran throughout FunctionParseContext.
public readonly struct CallOrRawCall
{
    public readonly CallNode? Call;
    public readonly RawCallNode? RawCall;

    public CallOrRawCall(CallNode call)
    {
        Call = call;
        RawCall = null;
    }

    public CallOrRawCall(RawCallNode rawCall)
    {
        Call = null;
        RawCall = rawCall;
    }

    public static implicit operator CallOrRawCall(CallNode call) => new(call);
    public static implicit operator CallOrRawCall(RawCallNode rawCall) => new(rawCall);

    public TextSpan Span => Call?.Span ?? RawCall!.Span;
}
