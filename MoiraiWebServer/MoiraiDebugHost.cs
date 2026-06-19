using Moirai.Core;
using Moirai.DebugAdapter;
using MoiraiWebServer.Hubs;

namespace MoiraiWebServer;

/// <summary>
/// Bridges the embedded DAP server to the web server's shared engine: the same static
/// <c>_db</c> and mutex that <see cref="ChatHub"/> uses, so a debug session is serialized
/// against web-UI-initiated runs.
/// </summary>
public sealed class MoiraiDebugHost : IDebugHost
{
    public Database Database => ChatHub.GetOrCreateDb();

    public string ProgramPath => Path.GetFullPath(Program.OptionsInstance.InputFile);

    public void RunDebugged(int years, DebugSession session, CancellationToken ct)
        => ChatHub.RunDebugged(years, session, ct);
}
