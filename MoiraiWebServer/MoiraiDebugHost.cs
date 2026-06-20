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
    // Non-blocking once the world exists: never wait on the engine mutex from the debugger's
    // protocol thread (a paused run holds it), which would deadlock continue/step.
    public Database Database => ChatHub.CurrentDb ?? ChatHub.GetOrCreateDb();

    public string ProgramPath => Path.GetFullPath(Program.OptionsInstance.InputFile);

    public void RunDebugged(int years, DebugSession session, CancellationToken ct)
        => ChatHub.RunDebugged(years, session, ct);

    public void AttachSession(DebugSession session) => ChatHub.AttachSession(session);

    public void DetachSession(DebugSession session) => ChatHub.DetachSession(session);
}
