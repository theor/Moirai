using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

internal class MyDocumentSymbolHandler : IDocumentSymbolHandler
{ private readonly ILogger _logger;
    private readonly MoiraiCache _moiraiCache;

    
    public MyDocumentSymbolHandler(ILogger<MyDeclarationHandler> logger, MoiraiCache moiraiCache)
    {
        _logger = logger;
        _moiraiCache = moiraiCache;
    }
    public async Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request,
        CancellationToken cancellationToken
    )
    {
        // you would normally get this from a common source that is managed by current open editor, current active editor, etc.
        return _moiraiCache.GetSymbols(request);

       
    }

    public DocumentSymbolRegistrationOptions GetRegistrationOptions(DocumentSymbolCapability capability, ClientCapabilities clientCapabilities) => new DocumentSymbolRegistrationOptions {
        DocumentSelector = MoiraiLanguage.Selector
    };
}
