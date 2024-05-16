using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

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
            // CompletionItem =
        };
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var line = _moiraiCache.GetLine(request.TextDocument.Uri, request.Position.Line);
        if (line == null)
            return Task.FromResult(new CompletionList());
        
        return Task.FromResult(new CompletionList(new CompletionItem
        {
            InsertText = "asd",
            Label = "in:" + line,
        }));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("CompletionItem");
    }
}
