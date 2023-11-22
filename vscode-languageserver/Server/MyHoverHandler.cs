using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

public class MyHoverHandler : HoverHandlerBase
{
    private readonly ILogger _logger;
    private readonly MoiraiCache _moiraiCache;

    
    public MyHoverHandler(ILogger<MyDeclarationHandler> logger, MoiraiCache moiraiCache)
    {
        _logger = logger;
        _moiraiCache = moiraiCache;
    }
    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = MoiraiLanguage.Selector,

        };
    }

    public override async Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        // _logger.LogCritical($"LINK {request.TextDocument} {request.Position}");
        var res = _moiraiCache.GetLocations(request.TextDocument, request.Position);
        if(res != null)
            return new Hover{ Range = res.Location.Range,
                Contents = new MarkedStringsOrMarkupContent(
                    new MarkedString("moirai", 
                        _moiraiCache.GetRange(request.TextDocument.Uri, res.Location.Range)))};
        return null;
    }
}
