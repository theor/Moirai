// See https://aka.ms/new-console-template for more information

using System.Collections.Immutable;
using System.Diagnostics;
using IntervalTree;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moirai.Parser;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using Serilog;
using Serilog.Events;
using ILogger = Serilog.ILogger;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

internal class Program
{
    private static void Main(string[] args)
    {
        // while (!Debugger.IsAttached)
        // {
        //     Thread.Sleep(100);
        // }
        MainAsync(args).Wait();
    }

    private static async Task MainAsync(string[] args)
    {
        // Debugger.Launch();
        // while (!Debugger.IsAttached)
        // {
        //     await Task.Delay(100);
        // }

        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.File("moirai-log.txt", rollingInterval: RollingInterval.Day, flushToDiskInterval:TimeSpan.FromSeconds(5))
            // .WriteTo.Debug(LogEventLevel.Debug)
            .MinimumLevel.Verbose()
            .CreateLogger();

        Log.Logger.Information("This only goes to file...");

        IObserver<WorkDoneProgressReport> workDone = null!;
        
        var server = await LanguageServer.From(
            options =>
                options
                    .WithInput(Console.OpenStandardInput())
                    .WithOutput(Console.OpenStandardOutput())
                    .ConfigureLogging(
                        x => x
                            .AddSerilog(Log.Logger)
                            .AddLanguageProtocolLogging()
                            .SetMinimumLevel(LogLevel.Trace)
                    )
                    .WithServices(x => x.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace)))
                    // .WithServices(x => x.AddLogging())
                    
                    .WithServices(
                        services =>
                        {
                            services.AddSingleton(
                                provider =>
                                {
                                    var loggerFactory = provider.GetService<ILoggerFactory>();
                                    var logger = loggerFactory.CreateLogger<MoiraiCache>();

                                    logger.LogInformation("Configuring");

                                    return new MoiraiCache(logger);
                                }
                            );
                            services.AddSingleton(
                                new ConfigurationItem
                                {
                                    Section = "typescript",
                                }
                            ).AddSingleton(
                                new ConfigurationItem
                                {
                                    Section = "terminal",
                                }
                            );
                        }
                    )
                 .OnStarted(
                            async (languageServer, token) =>
                            {
                                using var manager = await languageServer.WorkDoneManager.Create(new WorkDoneProgressBegin { Title = "Doing some work..." })
                                                                        .ConfigureAwait(false);

                                manager.OnNext(new WorkDoneProgressReport { Message = "doing things..." });
                                // await Task.Delay(10000).ConfigureAwait(false);
                                // manager.OnNext(new WorkDoneProgressReport { Message = "doing things... 1234" });
                                // await Task.Delay(10000).ConfigureAwait(false);
                                // manager.OnNext(new WorkDoneProgressReport { Message = "doing things... 56789" });

                                var logger = languageServer.Services.GetService<ILogger<MoiraiCache>>();
                                var configuration = await languageServer.Configuration.GetConfiguration(
                                    new ConfigurationItem
                                    {
                                        Section = "typescript",
                                    }, new ConfigurationItem
                                    {
                                        Section = "terminal",
                                    }
                                ).ConfigureAwait(false);

                                var baseConfig = new JObject();
                                foreach (var config in languageServer.Configuration.AsEnumerable())
                                {
                                    baseConfig.Add(config.Key, config.Value);
                                }

                                logger.LogInformation("Base Config: {@Config}", baseConfig);

                                var scopedConfig = new JObject();
                                foreach (var config in configuration.AsEnumerable())
                                {
                                    scopedConfig.Add(config.Key, config.Value);
                                }

                                logger.LogInformation("Scoped Config: {@Config}", scopedConfig);
                            }
                        )
                    .WithHandler<TextDocumentHandler>()
                    .WithHandler<MyDocumentSymbolHandler>()
                    // .WithHandler<CodeActionHandler>()
                    // .WithHandler<DocumentDiagnosticHandler>()
                    .WithHandler<MyWorkspaceSymbolsHandler>()
                    .WithHandler<MoiraiDocumentFormattingHandler>()
                    .WithHandler<SemanticTokensHandler>()
                    .WithHandler<MyDeclarationHandler>()
                    .WithHandler<MyHoverHandler>()
                    .WithHandler<MyUsageHandler>()
            ).ConfigureAwait(false);

            await server.WaitForExit.ConfigureAwait(false);
    }
}

public class MoiraiCache {
    private readonly ILogger<MoiraiCache> _logger;
    // private MoiraiDocument? _current;
    private Dictionary<DocumentUri, MoiraiDocument> _cache = new();
    public MoiraiCache(ILogger<MoiraiCache> logger)
    {
        logger.LogInformation("inside ctor");
        _logger = logger;
    }
    public async Task OnOpen(DidOpenTextDocumentParams notification)
    {
        var _current = new MoiraiDocument(notification.TextDocument.Uri, notification.TextDocument);
        _cache[_current.DocumentUri] = _current;
        await _current.Process(_logger);
    }
    public async Task OnChange(DidChangeTextDocumentParams notification)
    {
        if (_cache.TryGetValue(notification.TextDocument.Uri, out var doc))
        {
            doc.Apply(notification.ContentChanges, notification.TextDocument.Version);
            await doc.Process(_logger);
        }
    }
    public void PublishDiagnostics(DocumentUri textDocumentUri, ITextDocumentLanguageServer facadeTextDocument)
    {
        int version = 0;
        var diagnostics = ImmutableArray<Diagnostic>.Empty.ToBuilder();
        if (_cache.TryGetValue(textDocumentUri, out var doc))
        {
            version = doc.Version;
            if (doc.Errors != null)
                foreach (var error in doc.Errors)
                {
                    diagnostics.Add(new Diagnostic()
                    {
                        Code = "MR" + (int)error.Code,
                        Severity = DiagnosticSeverity.Error,
                        Message = error.Code + ": " + error.Message,
                        Range = new Range(error.Line - 1, error.Col, error.LineEnd - 1, error.ColEnd),
                        Source = "Moirai",

                    });
                }
        }
        

        facadeTextDocument.PublishDiagnostics(new PublishDiagnosticsParams()
        {
            Diagnostics = new Container<Diagnostic>(diagnostics.ToArray()),
            Uri = textDocumentUri, Version = version,
        });
    }

    // public string GetRange(Range r)
    // {
    //     
    // }
    public string GetContent(DocumentUri textDocumentUri)
    {
        if (_cache.TryGetValue(textDocumentUri, out var doc))
        {
            return doc.Content;
        }
        _logger.LogCritical("GetContent: NOT SAME URI");
            return "";
    }
    public void GetSemanticTokens(DocumentUri uri, SemanticTokensBuilder builder)
    {
        if (_cache.TryGetValue(uri, out var doc))
            foreach (var symbol in doc.SemanticTokens)
            {
                builder.Push(symbol.range, symbol.type, symbol.modifiers);
          
            }
    }

    public LocationOrLocationLink? GetLocations(TextDocumentIdentifier requestTextDocument,
        Position requestPosition)
    {
        if (_cache.TryGetValue(requestTextDocument.Uri, out var doc))
        {
            var loc = doc.Locations.Query(requestPosition);
            if (loc.Any())
                return
                    new LocationOrLocationLink(new Location{Range =  loc.First().FullDefinition, Uri = requestTextDocument.Uri});
        }
        return default;
    }

    public string GetRange(DocumentUri uri, Range locationRange)
    {
        if (_cache.TryGetValue(uri, out var doc))
        {
            var lines = doc.Content.Split('\n');
            return string.Join("\n",
                lines.Skip(locationRange.Start.Line).Take(1 + locationRange.End.Line - locationRange.Start.Line)).TrimEnd('\n', ' ');
        }

        return "";
    }
    // public Range? GetDefinitionRange(DocumentUri uri,Range locationRange)
    // {
    //     if (_cache.TryGetValue(uri, out var doc))
    //     {
    //         var loc = doc.Locations.FirstOrDefault(x => x.Item1.Contains(locationRange)).Item2;
    //         if (loc != null)
    //             return loc;
    //     }
    //
    //     return default;
    // }

    public void OnClose(DidCloseTextDocumentParams notification)
    {
        _cache.Remove(notification.TextDocument.Uri);
    }

    public SymbolInformationOrDocumentSymbolContainer? GetSymbols(DocumentSymbolParams request)
    {
        if (!_cache.TryGetValue(request.TextDocument.Uri, out var doc))
            return null;

        return doc.Symbols;
    }
}

internal class MoiraiDocument
{
    public readonly DocumentUri DocumentUri;
    private string _content;
    public string Content => _content;
    public int Version;

    public struct MoiraiSymbol
    {
        public Range Range;
        public Range FullRange;
        public string Name;
    }
    public List<(Range range, SemanticTokenType type, string[] modifiers)> SemanticTokens { get; set; } = new();
    public List<StoryParser.Error> Errors = new();
    public List<(Range, Range, Range)> Locations2 = new();
    public IntervalTree<Position, TokenVisitor.Definition> Locations = new();
    public SymbolInformationOrDocumentSymbolContainer? Symbols;

    public MoiraiDocument(DocumentUri documentUri, TextDocumentItem notificationTextDocument)
    {
        DocumentUri = documentUri;
        _content = notificationTextDocument.Text;
        Version = notificationTextDocument.Version.GetValueOrDefault();
    }
    public void Apply(IEnumerable<TextDocumentContentChangeEvent> changes, int? textDocumentVersion)
    {
        Version = textDocumentVersion.GetValueOrDefault();
        if(changes?.Any(c => c.Range != null) == true)
            throw new System.NotImplementedException("incremental changes not implemented yet");
        var change = changes.Last();
        _content = change.Text;
    }
    public Task Process(Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            var visitor = new TokenVisitor( logger, DocumentUri);
        
            StoryParser.SetupParser(Content, out var parser, visitor);
            var r = parser.r();
            r.Accept(visitor);
            Errors = visitor.Errors;
            SemanticTokens = visitor.SemanticTokens;
            Locations = visitor.Locations;
            Symbols = visitor.Symbols;
       
            var db = new Database();
            var astVisitor = new StoryParser.AstVisitor(db, parser);
            r.Accept(astVisitor);
            Errors.AddRange(astVisitor.Errors);
        }
        catch (Exception e)
        {
            Errors.Add(new StoryParser.Error(StoryParser.ErrorCode.Exception, 1,1, e.ToString()));
        }
        return Task.CompletedTask;
    }
}

// internal class DocumentDiagnosticHandler : DocumentDiagnosticHandlerBase
// {
//     protected override DiagnosticsRegistrationOptions CreateRegistrationOptions(DiagnosticClientCapabilities capability, ClientCapabilities clientCapabilities)
//     {
//         return new DiagnosticsRegistrationOptions();
//     }
//     public override Task<RelatedDocumentDiagnosticReport> Handle(DocumentDiagnosticParams request, CancellationToken cancellationToken)
//     {
// request.        
//     }
//     
// }
