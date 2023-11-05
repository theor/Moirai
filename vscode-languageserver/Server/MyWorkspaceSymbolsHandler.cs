using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Progress;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

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

    public async Task<Container<WorkspaceSymbol>?> Handle(
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