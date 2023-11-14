using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

public static class MoiraiLanguage
{
    public static readonly TextDocumentSelector Selector = TextDocumentSelector.ForLanguage("moirai");
}
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

public class MyDeclarationHandler : DefinitionHandlerBase
{
    private readonly ILogger _logger;
    private readonly MoiraiCache _moiraiCache;

    
    public MyDeclarationHandler(ILogger<MyDeclarationHandler> logger, MoiraiCache moiraiCache)
    {
        _logger = logger;
        _moiraiCache = moiraiCache;
    }

    // public async Task<LocationOrLocationLinks?> Handle(DeclarationParams request, CancellationToken cancellationToken)
    // {
    //     _logger.LogCritical($"LINK {request.TextDocument} {request.Position}");
    //     return new LocationOrLocationLinks(
    //         // _moiraiCache.GetLocations(request.TextDocument, request.Position)
    //         );
    // }
    //
    // public DeclarationRegistrationOptions GetRegistrationOptions(DeclarationCapability capability,
    //     ClientCapabilities clientCapabilities)
    // {
    //     return new DeclarationRegistrationOptions
    //     {
    //         DocumentSelector = TextDocumentSelector.ForLanguage("moirai"),
    //         
    //     };
    // }

    public override async Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        // _logger.LogCritical($"LINK {request.TextDocument} {request.Position}");
        var res = _moiraiCache.GetLocations(request.TextDocument, request.Position);
        if(res != null)
            return new LocationOrLocationLinks( res);
        return null;
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions
        {
            DocumentSelector = MoiraiLanguage.Selector,
            
        };
    }

}
