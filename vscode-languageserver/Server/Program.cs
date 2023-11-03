// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Progress;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using OmniSharp.Extensions.LanguageServer.Server;
using Serilog;
using Serilog.Events;
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
                            .SetMinimumLevel(LogLevel.Debug)
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
                    .WithHandler<SemanticTokensHandler>()
            ).ConfigureAwait(false);

            await server.WaitForExit.ConfigureAwait(false);
    }
}

public class MoiraiCache {
    private readonly ILogger<MoiraiCache> _logger;
    public List<StoryParser.Error>? Errors;
    private MoiraiDocument _current;

    public MoiraiCache(ILogger<MoiraiCache> logger)
    {
        logger.LogInformation("inside ctor");
        _logger = logger;
    }
    public void OnOpen(DidOpenTextDocumentParams notification)
    {
        _current = new MoiraiDocument(notification.TextDocument.Uri, notification.TextDocument);
    }
    public void OnChange(DidChangeTextDocumentParams notification)
    {
        _current.Apply(notification.ContentChanges, notification.TextDocument.Version);
    }
}

internal class MoiraiDocument
{
    public readonly DocumentUri DocumentUri;
    private string _content;
    public int Version;
    public MoiraiDocument(DocumentUri documentUri, TextDocumentItem notificationTextDocument)
    {
        DocumentUri = documentUri;
        _content = notificationTextDocument.Text;
        Version = notificationTextDocument.Version.GetValueOrDefault();
    }
    public void Apply(IEnumerable<TextDocumentContentChangeEvent> changes, int? textDocumentVersion)
    {
        Version = textDocumentVersion.GetValueOrDefault();
        if(changes.Any(c => c.Range != null))
            throw new System.NotImplementedException("incremental changes not implemented yet");
        var change = changes.Last();
        _content = change.Text;
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

internal class MyWorkspaceSymbolsHandler : IWorkspaceSymbolsHandler
    {
        private readonly IServerWorkDoneManager _serverWorkDoneManager;
        private readonly IProgressManager _progressManager;
        private readonly ILogger<MyWorkspaceSymbolsHandler> _logger;

        public MyWorkspaceSymbolsHandler(IServerWorkDoneManager serverWorkDoneManager, IProgressManager progressManager, ILogger<MyWorkspaceSymbolsHandler> logger)
        {
            _serverWorkDoneManager = serverWorkDoneManager;
            _progressManager = progressManager;
            _logger = logger;
        }

        public async Task<Container<WorkspaceSymbol>> Handle(
            WorkspaceSymbolParams request,
            CancellationToken cancellationToken
        )
        {
            using var reporter = _serverWorkDoneManager.For(
                request, new WorkDoneProgressBegin {
                    Cancellable = true,
                    Message = "This might take a while...",
                    Title = "Some long task....",
                    Percentage = 0
                }
            );
            using var partialResults = _progressManager.For(request, cancellationToken);
            if (partialResults != null)
            {
                // await Task.Delay(2000, cancellationToken).ConfigureAwait(false);

                reporter.OnNext(
                    new WorkDoneProgressReport {
                        Cancellable = true,
                        Percentage = 20
                    }
                );
                // await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                reporter.OnNext(
                    new WorkDoneProgressReport {
                        Cancellable = true,
                        Percentage = 40
                    }
                );
                // await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                reporter.OnNext(
                    new WorkDoneProgressReport {
                        Cancellable = true,
                        Percentage = 50
                    }
                );
                // await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                partialResults.OnNext(
                    new[] {
                        new WorkspaceSymbol {
                            ContainerName = "Partial Container",
                            Kind = SymbolKind.Constant,
                            Location = new Location {
                                Range = new Range(
                                    new Position(2, 1),
                                    new Position(2, 10)
                                )
                            },
                            Name = "Partial name"
                        }
                    }
                );

                reporter.OnNext(
                    new WorkDoneProgressReport {
                        Cancellable = true,
                        Percentage = 70
                    }
                );
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                reporter.OnNext(
                    new WorkDoneProgressReport {
                        Cancellable = true,
                        Percentage = 90
                    }
                );

                partialResults.OnCompleted();
                return new WorkspaceSymbol[] { };
            }

            try
            {
                return new[] {
                    new WorkspaceSymbol {
                        ContainerName = "Container",
                        Kind = SymbolKind.Constant,
                        Location = new Location {
                            Range = new Range(
                                new Position(1, 1),
                                new Position(1, 10)
                            )
                        },
                        Name = "name"
                    }
                };
            }
            finally
            {
                reporter.OnNext(
                    new WorkDoneProgressReport {
                        Cancellable = true,
                        Percentage = 100
                    }
                );
            }
        }

        public WorkspaceSymbolRegistrationOptions GetRegistrationOptions(WorkspaceSymbolCapability capability, ClientCapabilities clientCapabilities) => new WorkspaceSymbolRegistrationOptions();
    }