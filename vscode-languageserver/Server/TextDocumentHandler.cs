using System.Collections.Immutable;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

internal class TextDocumentHandler : TextDocumentSyncHandlerBase
{
    private readonly ILogger<TextDocumentHandler> _logger;
    private readonly ILanguageServerConfiguration _configuration;

    private readonly TextDocumentSelector _textDocumentSelector = new TextDocumentSelector(
        new TextDocumentFilter
        {
            Scheme = "file", 
            Pattern = "**/*.sg",
        }
    );

    private readonly ILanguageServerFacade _facade;
    private readonly MoiraiCache _moiraiCache;

    public TextDocumentHandler(ILogger<TextDocumentHandler> logger, ILanguageServerConfiguration configuration,
        ILanguageServerFacade facade, MoiraiCache moiraiCache)
    {
        _logger = logger;
        _configuration = configuration;
        _facade = facade;
        _moiraiCache = moiraiCache;
    }

    public TextDocumentSyncKind Change { get; } = TextDocumentSyncKind.Full;

    public override async Task<Unit> Handle(DidChangeTextDocumentParams notification, CancellationToken token)
    {
        _logger.LogCritical("DidChangeTextDocumentParams ");
        await _moiraiCache.OnChange(notification);
        _moiraiCache.PublishDiagnostics(_facade.TextDocument);

        return Unit.Value;
    }

    public override async Task<Unit> Handle(DidOpenTextDocumentParams notification, CancellationToken token)
    {
        await Task.Yield();
        _logger.LogCritical("DidOpenTextDocumentParams");
        await _moiraiCache.OnOpen(notification);
        // await _configuration.GetScopedConfiguration(notification.TextDocument.Uri, token).ConfigureAwait(false);

        _moiraiCache.PublishDiagnostics(_facade.TextDocument);


        return Unit.Value;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams notification, CancellationToken token)
    {
        if (_configuration.TryGetScopedConfiguration(notification.TextDocument.Uri, out var disposable))
        {
            disposable.Dispose();
        }

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams notification, CancellationToken token)
    {
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities) => new TextDocumentSyncRegistrationOptions()
    {
        DocumentSelector = _textDocumentSelector,
        Change = Change,
        Save = new SaveOptions() { IncludeText = true }
    };

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new TextDocumentAttributes(uri, "moirai");
}