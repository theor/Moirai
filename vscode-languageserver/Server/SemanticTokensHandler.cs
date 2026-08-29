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

    protected override Task Tokenize(
        SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier,
        CancellationToken cancellationToken
    )
    {
        _logger.LogCritical("Tokenize " + identifier.TextDocument.Uri);
        _moiraiCache.CurrentDoc = identifier.TextDocument.Uri;
        // you would normally get this from a common source that is managed by current open editor, current active editor, etc.
        _moiraiCache.GetSemanticTokens(identifier.TextDocument.Uri, builder);
        return Task.CompletedTask;
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
            DocumentSelector = MoiraiLanguage.Selector,
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
