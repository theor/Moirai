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

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        // var line = _moiraiCache.GetLine(request.TextDocument.Uri, request.Position.Line);
        // if (line == null)
        // return Task.FromResult(new CompletionList());
        MoiraiCodeCompletion.SetupMoiraiCompletion(_moiraiCache.GetContent(request.TextDocument.Uri), out var lexer,
            out var parser, out var core);

        int pos = MoiraiCodeCompletion.FindTokenIndex(parser, request.Position);
        IToken? t = parser.TokenStream.Get(pos);
        if (t == null)
            return Task.FromResult(new CompletionList());
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
        var completions = new List<CompletionItem>();
        foreach (var (key, value) in candidates.Rules)
        {
            var ruleName = parser.RuleNames[key];
            completions.Add(new CompletionItem
            {
                Label = "r:"+ruleName, InsertText = ruleName,
            });
        }
        foreach (var (key, value) in candidates.Tokens)
        {
            var tokenName = parser.Vocabulary.GetSymbolicName(key);
            completions.Add(new CompletionItem
            {
                Label = $"t:{tokenName} {string.Join(",", value.Select(parser.Vocabulary.GetSymbolicName))}", InsertText = tokenName,
            });
        }
        return Task.FromResult(new CompletionList(completions));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("CompletionItem");
    }
}