using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

public class MyHoverHandler : HoverHandlerBase
{
    private readonly ILogger _logger;
    private readonly MoiraiCache _moiraiCache;

    
    public MyHoverHandler(ILogger<MyHoverHandler> logger, MoiraiCache moiraiCache)
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
        var res = _moiraiCache.GetDefinition(request.TextDocument, request.Position);
        if(res != null)
        {
            var markedStrings = new List<MarkedString>();
            
            if(res.InlineDefinition != null)
                markedStrings.Add(new MarkedString("moirai",res.InlineDefinition));
            else if(res.FullDefinition != null)
                markedStrings.Add(new MarkedString("moirai",
                    _moiraiCache.GetRange(request.TextDocument.Uri, res.FullDefinition)));
            
            res.GetHoverText(markedStrings);
            return new Hover
            {
                Range = res.FullDefinition,
                Contents = new MarkedStringsOrMarkupContent(
                    markedStrings)
            };
        }
        return null;
    }
}
