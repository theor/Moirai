using Microsoft.Extensions.Logging;
using Moirai.Parser;
using Moirai.Parser.Ast;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Superpower.Model;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using Position = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;

internal class MoiraiDocumentFormattingHandler : DocumentFormattingHandlerBase
{
    private readonly ILogger<MoiraiDocumentFormattingHandler> _logger;
    private readonly MoiraiCache _moiraiCache;

    public MoiraiDocumentFormattingHandler(ILogger<MoiraiDocumentFormattingHandler> logger, MoiraiCache moiraiCache)
    {
        _logger = logger;
        _moiraiCache = moiraiCache;
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentFormattingCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentFormattingRegistrationOptions { DocumentSelector = MoiraiLanguage.Selector };
    }

    public override Task<TextEditContainer?> Handle(DocumentFormattingParams request,
        CancellationToken cancellationToken)
    {
        var content = _moiraiCache.GetContent(request.TextDocument.Uri);
        if (content == string.Empty)
            return Task.FromResult<TextEditContainer?>(null);

        var parse = StoryParser.ParseForTooling(content);
        var edits = MoiraiFormatter.Format(parse);
        edits.Sort((x, y) => x.Range.Start.CompareTo(y.Range.Start));
        return Task.FromResult<TextEditContainer?>(new TextEditContainer(edits));
    }
}

/// Whitespace-only formatter: it never moves or rewrites a token, it only adjusts the runs of spaces
/// between them, so the edits are safe to apply to a file the author is still typing in.
///
/// Structure is taken from the AST -- what counts as "the operator of this expression" or "the
/// closing brace of this scope" is a question about roles, not about token kinds, and the same
/// character can mean different things in different places. But the AST records spans only for the
/// constructs it models, not for punctuation, so the anchors themselves are found by searching the
/// token stream inside the span of the node that owns them. That is the job of Anchors below.
///
/// Trivia comes from MoiraiTokenizerResult.FullTokens, which keeps Space and Comment tokens the
/// grammar filters out -- the replacement for ANTLR's hidden channel, which is how the previous
/// implementation of this file reached the same whitespace.
internal sealed class MoiraiFormatter
{
    const int IndentWidth = 4;

    readonly Token<MoiraiTokenKind>[] _tokens;
    readonly int[] _starts;
    readonly List<TextEdit> _edits = new();
    int _indent;

    MoiraiFormatter(Token<MoiraiTokenKind>[] tokens)
    {
        _tokens = tokens;
        _starts = tokens.Select(t => t.Span.Position.Absolute).ToArray();
    }

    public static List<TextEdit> Format(StoryParser.ToolingParse parse)
    {
        var formatter = new MoiraiFormatter(parse.Tokens.FullTokens.ToArray());
        foreach (var def in parse.Defs)
            formatter.Def(def);
        return formatter._edits;
    }

    int IndentCount() => _indent * IndentWidth;

    // ---- Anchors: finding punctuation the AST does not record -----------------------------

    static int Start(TextSpan span) => span.Position.Absolute;
    static int End(TextSpan span) => span.Position.Absolute + span.Length;

    /// First token of `kind` whose start lies in [from, to).
    int? Find(MoiraiTokenKind kind, int from, int to)
    {
        for (int i = LowerBound(from); i < _tokens.Length && _starts[i] < to; i++)
            if (_tokens[i].Kind == kind)
                return i;
        return null;
    }

    /// Last token of `kind` whose start lies in [from, to).
    int? FindLast(MoiraiTokenKind kind, int from, int to)
    {
        int? found = null;
        for (int i = LowerBound(from); i < _tokens.Length && _starts[i] < to; i++)
            if (_tokens[i].Kind == kind)
                found = i;
        return found;
    }

    /// The first token of a construct: the anchor an indentation rule applies to.
    int? FirstIn(TextSpan span)
    {
        for (int i = LowerBound(Start(span)); i < _tokens.Length && _starts[i] < End(span); i++)
            if (!IsTrivia(_tokens[i].Kind))
                return i;
        return null;
    }

    int LowerBound(int absolute)
    {
        int lo = 0, hi = _starts.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_starts[mid] < absolute) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    static bool IsTrivia(MoiraiTokenKind kind) =>
        kind is MoiraiTokenKind.Space or MoiraiTokenKind.Comment;

    // ---- Whitespace edits -----------------------------------------------------------------

    /// Makes the space run on either side of a token exactly the requested width; null means "leave
    /// this side alone".
    ///
    /// Note the asymmetry inherited from the previous implementation, which the golden files pin: an
    /// existing run is only rewritten if it actually contains a Space, and a missing run is only
    /// filled in when there was no trivia at all. So a token preceded solely by a comment is left
    /// untouched rather than having a space forced in front of it.
    void EnsureSpaces(int? index, int? right, int? left)
    {
        if (index is not { } i)
            return;

        if (left is { } leftCount)
        {
            var run = TriviaRun(i, -1);
            if (run.Count > 0)
            {
                foreach (var t in run)
                    if (t.Kind == MoiraiTokenKind.Space && t.Span.Length != leftCount)
                        Replace(t, leftCount);
            }
            else if (leftCount > 0)
            {
                Insert(StartPosition(_tokens[i]), leftCount);
            }
        }

        if (right is { } rightCount)
        {
            var run = TriviaRun(i, +1);
            if (run.Count > 0)
            {
                foreach (var t in run)
                    if (t.Kind == MoiraiTokenKind.Space && t.Span.Length != rightCount)
                        Replace(t, rightCount);
            }
            else if (rightCount > 0)
            {
                Insert(EndPosition(_tokens[i]), rightCount);
            }
        }
    }

    /// The contiguous Space/Comment tokens adjacent to `index` in `step` direction. A line break is
    /// not trivia, so a run never crosses one -- which is what makes "the space to my left" mean the
    /// indentation when the token starts a line.
    List<Token<MoiraiTokenKind>> TriviaRun(int index, int step)
    {
        var run = new List<Token<MoiraiTokenKind>>();
        for (int i = index + step; i >= 0 && i < _tokens.Length && IsTrivia(_tokens[i].Kind); i += step)
            run.Add(_tokens[i]);
        if (step < 0)
            run.Reverse();
        return run;
    }

    // Superpower positions are 1-based on both axes; LSP is 0-based on both.
    static Position StartPosition(Token<MoiraiTokenKind> t) =>
        new(t.Span.Position.Line - 1, t.Span.Position.Column - 1);

    static Position EndPosition(Token<MoiraiTokenKind> t) =>
        new(t.Span.Position.Line - 1, t.Span.Position.Column - 1 + t.Span.Length);

    void Replace(Token<MoiraiTokenKind> t, int spaces) =>
        _edits.Add(new TextEdit
        {
            NewText = new string(' ', spaces),
            Range = new Range(StartPosition(t), EndPosition(t)),
        });

    void Insert(Position at, int spaces) =>
        _edits.Add(new TextEdit { NewText = new string(' ', spaces), Range = new Range(at, at) });

    // ---- Traversal ------------------------------------------------------------------------

    void Def(DefNode def)
    {
        foreach (var attribute in def.Attributes)
            EnsureSpaces(Find(MoiraiTokenKind.At, Start(attribute.Span), End(attribute.Span)), 0, 0);

        if (def.Event is { } ev)
        {
            EnsureSpaces(Find(MoiraiTokenKind.Event, Start(ev.Span), End(ev.Span)), 1, 0);
            EnsureSpaces(IndexOfIdent(ev.Name), 1, null);
            Scope(ev.Scope);
        }
        else if (def.Trigger is { } trigger)
        {
            Scope(trigger.Scope);
        }
        else if (def.TypeDefinition is { } typeDef)
        {
            foreach (var prop in typeDef.PropDefinitions)
                PropDefinition(prop);
            foreach (var fn in typeDef.FunctionDefinitions)
                Scope(fn.Scope);
        }
        else if (def.FunctionDefinition is { } fn)
        {
            Scope(fn.Scope);
        }
        else if (def.TableDefinition is { } table)
        {
            foreach (var entry in table.Entries)
                Value(entry.Value);
        }
    }

    int? IndexOfIdent(Ident ident) => FirstIn(ident.Span);

    void PropDefinition(PropDefinitionNode prop)
    {
        int from = Start(prop.Span), to = End(prop.Span);
        // Properties only ever sit one level inside a type body, so the previous implementation
        // hard-coded their indentation rather than tracking depth. Kept, because the goldens pin it.
        EnsureSpaces(Find(MoiraiTokenKind.Prop, from, to), 1, IndentWidth);
        EnsureSpaces(IndexOfIdent(prop.PropertyId), 0, null);
        EnsureSpaces(Find(MoiraiTokenKind.Colon, from, to), 1, null);
        EnsureSpaces(IndexOfIdent(prop.Type.Name), 0, null);
    }

    void Scope(ScopeNode scope)
    {
        _indent++;
        if (scope.When is { } when)
            foreach (var e in when.Exprs)
                Expr(e);
        if (scope.WhenCreated is { } whenCreated)
            foreach (var e in whenCreated.Exprs)
                Expr(e);
        foreach (var effect in scope.Effects)
            Effect(effect);
        _indent--;

        EnsureSpaces(FindLast(MoiraiTokenKind.ScopeClose, Start(scope.Span), End(scope.Span)), null, IndentCount());
    }

    void Effect(EffectNode? effect)
    {
        if (effect == null)
            return;

        EnsureSpaces(FirstIn(effect.Span), null, IndentCount());

        if (effect.Set is { } set)
        {
            // The `=` is whatever sits between the assigned path and the value.
            EnsureSpaces(Find(MoiraiTokenKind.Eq, End(set.Path.Span), Start(set.Expr.Span)), 1, 1);
            Expr(set.Expr);
        }

        if (effect.Init is { } init)
        {
            EnsureSpaces(Find(MoiraiTokenKind.ColonEq, End(init.PropertyId.Span), Start(init.Expr.Span)), 1, 1);
            Expr(init.Expr);
        }

        if (effect.Var is { } var)
            Expr(var.Expr);

        Expr(effect.Expr);
    }

    void Expr(ExprNode? expr)
    {
        if (expr == null)
            return;

        if (expr.If is { } ifNode)
        {
            Expr(ifNode.Cond);
            Scope(ifNode.Then);
            if (ifNode.Else is { } elseScope)
            {
                EnsureSpaces(Find(MoiraiTokenKind.Else, End(ifNode.Then.Span), Start(elseScope.Span)), 1, 1);
                Scope(elseScope);
            }

            return;
        }

        if (expr.Match is { } match)
        {
            foreach (var e in match.Exprs)
                Expr(e);
            foreach (var c in match.Cases)
                MatchCase(c);
            return;
        }

        if (expr.Value is { } value)
        {
            Value(value);
            return;
        }

        if (expr.Paren is { } paren)
        {
            Expr(paren);
            return;
        }

        if (expr.Left is { } left && expr.Right is { } right)
        {
            Expr(left);
            // The operator token is the one non-trivia token between the two operands.
            EnsureSpaces(FirstNonTriviaBetween(End(left.Span), Start(right.Span)), 1, 1);
            Expr(right);
        }
    }

    int? FirstNonTriviaBetween(int from, int to)
    {
        for (int i = LowerBound(from); i < _tokens.Length && _starts[i] < to; i++)
            if (!IsTrivia(_tokens[i].Kind) && _tokens[i].Kind != MoiraiTokenKind.LineBreak)
                return i;
        return null;
    }

    void MatchCase(MatchCaseNode c)
    {
        _indent++;
        EnsureSpaces(FirstIn(c.Span), null, IndentCount());
        EnsureSpaces(Find(MoiraiTokenKind.Arrow, Start(c.Span), End(c.Span)), 1, 1);
        if (c.Scope is { } scope)
            Scope(scope);
        _indent--;
    }

    void Value(ValueNode? value)
    {
        if (value == null)
            return;

        if (value.Call is { } call)
            Call(call);

        if (value.RawCall is { } rawCall)
        {
            Value(rawCall.Value);
            if (rawCall.Scope is { } s)
                Scope(s);
        }

        if (value.StringLit is { } str)
            foreach (var part in str.Parts)
                if (part is StringExprPart exprPart)
                    Expr(exprPart.Expr);

        if (value.Path is { } path)
            foreach (var dot in path.DotProperties)
                if (dot.Call is { } dotCall)
                    Call(dotCall);
    }

    void Call(CallNode call)
    {
        int from = Start(call.Span), to = End(call.Span);
        var open = Find(MoiraiTokenKind.ParenOpen, from, to);
        var close = FindLast(MoiraiTokenKind.ParenClose, from, to);

        if (call.VarId is { } varId)
        {
            EnsureSpaces(IndexOfIdent(varId), 0, 1);
            // The `:` introducing the initializer sits between the variable and the argument list.
            EnsureSpaces(Find(MoiraiTokenKind.Colon, End(varId.Span), open is { } o ? _starts[o] + 1 : to), 1, 0);
        }
        else
        {
            EnsureSpaces(IndexOfIdent(call.FunId), 0, null);
        }

        EnsureSpaces(open, 0, null);
        EnsureSpaces(close, null, 0);

        foreach (var arg in call.Args)
            Expr(arg);
        if (call.Scope is { } scope)
            Scope(scope);
    }
}
