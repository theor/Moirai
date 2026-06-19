using Moirai.Core;

namespace Moirai.DebugAdapter;

/// <summary>
/// The bridge the host (e.g. MoiraiWebServer) provides to the debug adapter: access to the live
/// <see cref="Database"/> and a way to run a debugged simulation under the host's own concurrency
/// guard (so a debug session does not race other clients).
/// </summary>
public interface IDebugHost
{
    Database Database { get; }

    /// <summary>Absolute path of the story file being debugged (used for DAP source mapping).</summary>
    string ProgramPath { get; }

    /// <summary>
    /// Install <paramref name="session"/> as the engine's debug hook and run a simulation of
    /// <paramref name="years"/> years under the host's lock. Called on a worker thread; blocks until
    /// the pass completes, is cancelled, or the session terminates. Must clear the hook on exit.
    /// </summary>
    void RunDebugged(int years, DebugSession session, CancellationToken ct);
}
