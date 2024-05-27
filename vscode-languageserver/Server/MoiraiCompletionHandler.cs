using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Antlr4CodeCompletion.Core.CodeCompletion;
using Microsoft.Extensions.Logging;
using Moirai.Parser;
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

    public override async Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        // var line = _moiraiCache.GetLine(request.TextDocument.Uri, request.Position.Line);
        // if (line == null)
        // return Task.FromResult(new CompletionList());
        MoiraiCodeCompletion.SetupMoiraiCompletion(_moiraiCache.GetContent(request.TextDocument.Uri), out var lexer,
            out var parser, out var core);

        int pos = MoiraiCodeCompletion.FindTokenIndex(parser, request.Position);
        IToken? t = parser.TokenStream.Get(pos);
        if (t == null)
            return new CompletionList();
        // foreach (var t1 in lexer.GetAllTokens())
        // {
        //     if (TokenVisitor.GetRange(t1).Contains(request.Position))
        //     {
        //         t = t1;
        //         
        //         break;
        //     }
        // }

        int tokenIndex = t.TokenIndex;
        _logger.LogCritical($"Token index: {tokenIndex} token {t.Text}");
        var candidates = core.CollectCandidates(tokenIndex, null);
        if(!_moiraiCache.GetDocument(request.TextDocument.Uri, out var document))
            return new CompletionList();
        return new CompletionList(await MoiraiCodeCompletion.Complete(_logger, parser,candidates, document!, request.Position, tokenIndex));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("CompletionItem");
    }
}
