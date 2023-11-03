using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

public class SemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly ILogger _logger;

    private readonly MoiraiCache _moiraiCache;

    public SemanticTokensHandler(ILogger<SemanticTokensHandler> logger, MoiraiCache moiraiCache)
    {
        _logger = logger;
        _moiraiCache = moiraiCache;
    }

    public override async Task<SemanticTokens?> Handle(
        SemanticTokensParams request, CancellationToken cancellationToken
    )
    {
        var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override async Task<SemanticTokens?> Handle(
        SemanticTokensRangeParams request, CancellationToken cancellationToken
    )
    {
        var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override async Task<SemanticTokensFullOrDelta?> Handle(
        SemanticTokensDeltaParams request,
        CancellationToken cancellationToken
    )
    {
        var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    protected override async Task Tokenize(
        SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier,
        CancellationToken cancellationToken
    )
    {
        _logger.LogCritical("Tokenize");
        // you would normally get this from a common source that is managed by current open editor, current active editor, etc.
        _moiraiCache.GetSymbols(identifier.TextDocument.Uri, builder);

    }

    protected override Task<SemanticTokensDocument>
        GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));
    }

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability, ClientCapabilities clientCapabilities
    )
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("moirai"),
            Legend = new SemanticTokensLegend
            {
                TokenModifiers = capability.TokenModifiers,
                TokenTypes = capability.TokenTypes
            },
            Full = new SemanticTokensCapabilityRequestFull
            {
                Delta = true
            },
            Range = true
        };
    }
}

class TokenVisitor : MoiraiBaseVisitor<object?>, StoryParser.IVisitor
{
    private readonly ILogger _logger;
    public readonly List<(Range range, SemanticTokenType type, string[] modifiers)> Symbols = new();
    public List<StoryParser.Error> Errors { get; } = new();
    public TokenVisitor(ILogger logger)
    {
        _logger = logger;
    }

    private void PushSymbol(IToken symbol, SemanticTokenType tokenType, params string[] keyword)
    {
        Symbols.Add((
            new Range(symbol.Line - 1, symbol.Column, symbol.Line - 1, symbol.Column + symbol.Text.Length),
            tokenType,
            keyword));
    }
    public override object? VisitAction(Moirai.ActionContext context)
    {
        var id = context.ID();
        PushSymbol(context.RULE().Symbol, SemanticTokenType.Keyword);
        PushSymbol(id.Symbol, SemanticTokenType.Class, SemanticTokenModifier.Definition);
        return base.VisitAction(context);
    }
    public override object? VisitEvent(Moirai.EventContext context)
    {
        var id = context.ID();
        PushSymbol(context.EVENT().Symbol, SemanticTokenType.Keyword);
        PushSymbol(id.Symbol, SemanticTokenType.Class, SemanticTokenModifier.Definition);
        return base.VisitEvent(context);
    }

    public override object? VisitProp_definition(Moirai.Prop_definitionContext context)
    {
        PushSymbol(context.PROP().Symbol, SemanticTokenType.Keyword);
        return base.VisitProp_definition(context);
    }
    public override object? VisitType_definition(Moirai.Type_definitionContext context)
    {
        PushSymbol(context.ENTITY().Symbol, SemanticTokenType.Keyword);
        PushSymbol(context.TYPE_ID().Symbol, SemanticTokenType.Type);
        return base.VisitType_definition(context);
    }
    public override object? VisitEnum_definition(Moirai.Enum_definitionContext context)
    {
        PushSymbol(context.ENUM().Symbol, SemanticTokenType.Keyword);

        PushSymbol(context.TYPE_ID(0).Symbol, SemanticTokenType.Type);
        foreach (var member in context.TYPE_ID().Skip(1))
        {
            PushSymbol(member.Symbol, SemanticTokenType.EnumMember);
        }        
        return base.VisitEnum_definition(context);
    }
    public override object? VisitCall(Moirai.CallContext context)
    {
        PushSymbol(context.ID().Symbol, SemanticTokenType.Function);
        return base.VisitCall(context);
    }
    public override object? VisitCall_assign(Moirai.Call_assignContext context)
    {
        PushSymbol(context.ID().Symbol, SemanticTokenType.Function);
        if (context.VAR_ID() != null)
            PushSymbol(context.VAR_ID().Symbol, SemanticTokenType.Variable);
        return base.VisitCall_assign(context);
    }
    public override object? VisitTerminal(ITerminalNode node)
    {

        if (node.Parent is Moirai.SetContext && node.Symbol.Text == "set")
        {
            PushSymbol(node.Symbol, SemanticTokenType.Keyword);
        }
        if (node.Parent is Moirai.WhenContext && node.Symbol.Text == "when")
        {
            PushSymbol(node.Symbol, SemanticTokenType.Keyword);
        }
        return base.VisitTerminal(node);
    }
    public override object? VisitPath(Moirai.PathContext context)
    {
        if (context.VAR_ID() != null)
        {
            PushSymbol(context.VAR_ID().Symbol, SemanticTokenType.Variable);
        }
        return base.VisitPath(context);
    }
    public override object? VisitExpr(Moirai.ExprContext context)
    {
        if (context.value() != null)
            return context.value().Accept(this);
        if (context.paren_expr != null)
            return context.paren_expr.Accept(this);

        if (context.op != null)
        {
            PushSymbol(context.op, SemanticTokenType.Operator);
        }
        return base.VisitExpr(context);
    }
    public override object? VisitValue(Moirai.ValueContext context)
    {
        if (context.TYPE_ID() != null)
            PushSymbol(context.Start, SemanticTokenType.Type);
        else if (context.@string() != null)
            PushSymbol(context.Start, SemanticTokenType.String);
        else if (context.@bool() != null || context.number() != null || context.NULL() != null)
            PushSymbol(context.Start, SemanticTokenType.Number);
        else
            return base.VisitValue(context);

        return null;
    }
}