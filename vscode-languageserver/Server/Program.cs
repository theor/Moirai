// See https://aka.ms/new-console-template for more information

using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using IntervalTree;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moirai.Parser;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
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
            .WriteTo.File("moirai-log.txt", rollingInterval: RollingInterval.Day,
                flushToDiskInterval: TimeSpan.FromSeconds(5))
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
                            using var manager = await languageServer.WorkDoneManager.Create(new WorkDoneProgressBegin
                                    { Title = "Doing some work..." })
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
                    .WithHandler<MoiraiCompletionHandler>()
                    // .WithHandler<CodeActionHandler>()
                    // .WithHandler<DocumentDiagnosticHandler>()
                    .WithHandler<MyWorkspaceSymbolsHandler>()
                    .WithHandler<MoiraiDocumentFormattingHandler>()
                    .WithHandler<SemanticTokensHandler>()
                    .WithHandler<MyDeclarationHandler>()
                    .WithHandler<MyHoverHandler>()
                    .WithHandler<MyUsageHandler>()
                    .WithHandler<MoiraiCodeLensHandler>()
                    .WithHandler<MoiraiCommandHandler>()
        ).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);
    }
}

internal class MoiraiCommandHandler : ExecuteTypedCommandHandlerBase<string>
{
    private readonly MoiraiCache _moiraiCache;
    private readonly ILogger<MoiraiCache> _logger;
    private readonly string _commandName;

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(ExecuteCommandCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions
        {
            Commands = new Container<string>("moirai.servercommand")
        };
    }

    public override Task<Unit> Handle(string param, CancellationToken cancellationToken)
    {
        _logger.LogCritical($"HANDLE COMMAND2 {_commandName} {param} from {_moiraiCache.CurrentDoc}");
        if(!_moiraiCache.GetDocument(_moiraiCache.CurrentDoc, out var doc))
            return Task.FromResult(Unit.Value);
        // doc.Linker.GetDefinitionAt()
        return Task.FromResult(Unit.Value);
        
    }

    public MoiraiCommandHandler(MoiraiCache moiraiCache, ISerializer serializer, ILogger<MoiraiCache> logger) : base("moirai.servercommand", serializer)
    {
        _commandName = "moirai.servercommand";
        _moiraiCache = moiraiCache;
        _logger = logger;
    }
}

public class MoiraiCache
{
    private readonly ILogger<MoiraiCache> _logger;

    // private MoiraiDocument? _current;
    private Dictionary<DocumentUri, MoiraiDocument> _cache = new();

    public MoiraiCache(ILogger<MoiraiCache> logger)
    {
        logger.LogInformation("inside ctor");
        _logger = logger;
    }

    public DocumentUri CurrentDoc { get; set; }

    public async Task OnOpen(DidOpenTextDocumentParams notification)
    {
        var current = new MoiraiDocument(notification.TextDocument.Uri, notification.TextDocument);
        CurrentDoc = notification.TextDocument.Uri;
        _cache[current.DocumentUri] = current;
        await current.Process(_logger);
    }

    public async Task OnChange(DidChangeTextDocumentParams notification)
    {
        if (_cache.TryGetValue(notification.TextDocument.Uri, out var doc))
        {
            CurrentDoc = notification.TextDocument.Uri;
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
                        Severity = error.Severity switch
                        {
                            StoryParser.Severity.Warning => DiagnosticSeverity.Warning,
                            StoryParser.Severity.Information => DiagnosticSeverity.Information,
                            _ => DiagnosticSeverity.Error,
                        },
                        // Fade out redundant code (e.g. a `type = T` filter that repeats the iteration's type).
                        Tags = error.Code == StoryParser.ErrorCode.RedundantTypeFilter
                            ? new Container<DiagnosticTag>(DiagnosticTag.Unnecessary)
                            : null,
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

    public MoiraiSymbol.Definition? GetDefinition(TextDocumentIdentifier requestTextDocument,
        Position requestPosition)
    {
        if (_cache.TryGetValue(requestTextDocument.Uri, out var doc))
        {
            return doc.Linker.GetDefinitionAt(requestPosition);
            // var loc = doc.Definitions(requestPosition);
            // return loc.FirstOrDefault();
        }

        return default;
    }

    public IEnumerable<Location> GetReferences(TextDocumentIdentifier requestTextDocument,
        Position requestPosition, bool includeDeclaration)
    {
        if (_cache.TryGetValue(requestTextDocument.Uri, out var doc))
        {
            return doc.Linker.GetReferences(requestPosition, includeDeclaration)
                .Select(range => new Location { Uri = requestTextDocument.Uri, Range = range });
        }

        return Enumerable.Empty<Location>();
    }

    public IEnumerable<(Range nameRange, int usageCount)> GetDeclarationUsages(
        TextDocumentIdentifier requestTextDocument)
    {
        if (_cache.TryGetValue(requestTextDocument.Uri, out var doc))
            return doc.Linker.GetDeclarationUsages();

        return Enumerable.Empty<(Range, int)>();
    }

    public IEnumerable<(Range range, string title)> GetTriggerReadPropLenses(
        TextDocumentIdentifier requestTextDocument)
    {
        if (_cache.TryGetValue(requestTextDocument.Uri, out var doc))
            return doc.TriggerReadPropLenses;

        return Enumerable.Empty<(Range, string)>();
    }

    public string? GetLine(DocumentUri uri, int line)
    {
        if (_cache.TryGetValue(uri, out var doc))
        {
            var lines = doc.Content.Split('\n');
            return lines[line];
        }

        return null;
    }

    public string GetRange(DocumentUri uri, Range locationRange)
    {
        if (_cache.TryGetValue(uri, out var doc))
        {
            var lines = doc.Content.Split('\n');
            return string.Join("\n",
                    lines.Skip(locationRange.Start.Line).Take(1 + locationRange.End.Line - locationRange.Start.Line))
                .TrimEnd('\n', ' ');
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

    public bool GetDocument(DocumentUri textDocumentUri, out MoiraiDocument? doc)
    {
        if (!_cache.TryGetValue(textDocumentUri, out doc)) return false;
        return true;
    }
}

public class MoiraiDocument
{
    public readonly DocumentUri DocumentUri;
    private string _content;
    public string Content => _content;
    public int Version;

    public List<(Range range, SemanticTokenType type, string[] modifiers)> SemanticTokens { get; set; } = new();
    public List<StoryParser.Error> Errors = new();
    // Per-trigger CodeLens text showing the properties whose changes the trigger reacts to (drives the
    // engine's property-gated dispatch). Computed during Process, where both the parse tree and the
    // built Database are in scope. Range = the trigger's name token.
    public readonly List<(Range range, string title)> TriggerReadPropLenses = new();
    // private IntervalTree<Position, MoiraiSymbol.Definition> _locations = new();
    public SymbolInformationOrDocumentSymbolContainer Symbols = new();

    /// The world built by the last parse. Completion reads types, enums, tables and functions off
    /// it; it is kept per-document rather than taken from the mutable static Database.Instance,
    /// which belongs to whichever document was processed most recently.
    public Database? Database;

    public MoiraiDocument(DocumentUri documentUri, TextDocumentItem notificationTextDocument)
    {
        DocumentUri = documentUri;
        _content = notificationTextDocument.Text;
        Version = notificationTextDocument.Version.GetValueOrDefault();
    }

    public void Apply(IEnumerable<TextDocumentContentChangeEvent> changes, int? textDocumentVersion)
    {
        Version = textDocumentVersion.GetValueOrDefault();
        if (changes?.Any(c => c.Range != null) == true)
            throw new System.NotImplementedException("incremental changes not implemented yet");
        var change = changes.Last();
        _content = change.Text;
    }

    public Task Process(Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            // The linker is built from inside the parse: SourceLinker's constructor seeds itself
            // with the builtin types and functions off Database.Instance, which only exists once
            // the parse has created the Database.
            var parse = StoryParser.ParseForTooling(Content, _ => Linker = new SourceLinker());
            Database = parse.Database;

            SemanticTokens.Clear();
            List<SymbolInformationOrDocumentSymbol> symbols = new();
            MoiraiSemanticTokens.Build(parse, Linker, DocumentUri, SemanticTokens, symbols);
            Symbols = symbols;

            Errors = new List<StoryParser.Error>(parse.Errors);
            Errors.AddRange(parse.Visitor.InfoMarkers); // Information-severity annotations (e.g. SQL-inlined calls)

            // A "reads: <props>" CodeLens over each `when Changed` trigger, surfacing the read-property
            // set the engine uses for property-gated dispatch. Matched by name from the parsed
            // trigger definitions (which carry source spans) to the built Database's EventTriggers.
            TriggerReadPropLenses.Clear();
            var db = parse.Database;
            foreach (var def in parse.Defs)
            {
                if (def.Trigger is not { } triggerNode)
                    continue;
                var trig = db.Triggers.FirstOrDefault(t => t.Name == triggerNode.Name.Text);
                if (trig == null)
                    continue;

                string title;
                if (trig.When.Item1 == EventTrigger.WhenType.Created)
                {
                    // when_created: fires when an entity of the type is created (no property gating).
                    title = "reacts to new " + db.GetEntityTypeName(trig.When.Item2);
                }
                else
                {
                    var gating = db.GetTriggerGatingProps(trig);
                    if (gating is { Length: > 0 })
                        title = "reads: " + string.Join(", ",
                            gating.Select(db.GetPropertyName).OrderBy(n => n, System.StringComparer.Ordinal));
                    else if (trig.When.Item3 == null)
                        title = "reacts to every change"; // no predicate — fires on any change of its type
                    else
                        // Has a predicate, but it uses constructs we can't read through (a function call,
                        // etc.), so it can't be gated and is evaluated on every change. Flag it so the
                        // author can simplify/guard it if the trigger is hot.
                        title = "reacts to every change (predicate not gated)";
                }

                TriggerReadPropLenses.Add((new FileRange(triggerNode.Name.Span).ToLspRange(), title));
            }
        }
        catch (Exception e)
        {
            Errors.Add(new StoryParser.Error(StoryParser.ErrorCode.Exception, 1, 1, e.ToString()));
        }

        return Task.CompletedTask;
    }

    public SourceLinker Linker { get; set; }

    // public IEnumerable<MoiraiSymbol.Definition> Definitions(Position position, MoiraiSymbol.DefinitionType? definitionType = null)
    // {
    //     return _locations.Query(position).Where(d => definitionType == null || d.Type == definitionType);
    // }
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
