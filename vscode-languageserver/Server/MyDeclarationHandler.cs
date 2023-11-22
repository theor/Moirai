using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

public static class MoiraiLanguage
{
    public static readonly TextDocumentSelector Selector = TextDocumentSelector.ForLanguage("moirai");
}
public class MyUsageHandler : ReferencesHandlerBase
{
    private readonly ILogger _logger;
    private readonly MoiraiCache _moiraiCache;

    
    public MyUsageHandler(ILogger<MyDeclarationHandler> logger, MoiraiCache moiraiCache)
    {
        _logger = logger;
        _moiraiCache = moiraiCache;
    }
    protected override ReferenceRegistrationOptions CreateRegistrationOptions(ReferenceCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new ReferenceRegistrationOptions { DocumentSelector = MoiraiLanguage.Selector };
    }

    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        return null;// _moiraiCache.GetLocations(request.Position)
        // request.Context.IncludeDeclaration
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
