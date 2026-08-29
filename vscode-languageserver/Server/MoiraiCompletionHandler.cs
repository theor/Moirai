using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// Thin transport layer: the work is in MoiraiCompletion, which reads the token stream around the
/// caret rather than the parse tree (see the reasoning there -- the definition you are typing in is
/// the one that does not parse).
public class MoiraiCompletionHandler : CompletionHandlerBase
{
    private readonly ILogger _logger;
    private readonly MoiraiCache _moiraiCache;

    public MoiraiCompletionHandler(ILogger<MoiraiCompletionHandler> logger, MoiraiCache moiraiCache)
    {
        _logger = logger;
        _moiraiCache = moiraiCache;
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = MoiraiLanguage.Selector,
            TriggerCharacters = new Container<string>("."),
        };
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        if (!_moiraiCache.GetDocument(request.TextDocument.Uri, out var document) || document == null)
            return Task.FromResult(new CompletionList());

        return Task.FromResult(new CompletionList(MoiraiCompletion.Complete(document, request.Position)));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken) =>
        Task.FromResult(request);
}
