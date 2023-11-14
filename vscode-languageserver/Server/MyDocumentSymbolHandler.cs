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
        return null;
        // you would normally get this from a common source that is managed by current open editor, current active editor, etc.
        var content = _moiraiCache.GetContent(request.TextDocument.Uri);
        var lines = content.Split('\n');
        var symbols = new List<SymbolInformationOrDocumentSymbol>();
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.StartsWith("rule "))
            {
                var end = line.IndexOf('{') - 1;
                symbols.Add(
                    new DocumentSymbol {
                        // Detail = ,
                        // Deprecated = true,
                        Kind = SymbolKind.Class,
                        // Tags = new[] { SymbolTag.Deprecated },
                        Range = new Range(
                            new Position(lineIndex+1, 0),
                            new Position(lineIndex+1, end)
                        ),
                        SelectionRange =
                            new Range(
                                new Position(lineIndex+1, 0),
                                new Position(lineIndex+1, end)
                            ),
                        Name = line.Substring(5, end - 5)
                    }
                );
            }
        }

        // await Task.Delay(2000, cancellationToken);
        return symbols;
    }

    public DocumentSymbolRegistrationOptions GetRegistrationOptions(DocumentSymbolCapability capability, ClientCapabilities clientCapabilities) => new DocumentSymbolRegistrationOptions {
        DocumentSelector = MoiraiLanguage.Selector
    };
}
