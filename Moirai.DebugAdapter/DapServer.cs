using System.Text.Json.Nodes;
using Moirai.Core;

namespace Moirai.DebugAdapter;

/// <summary>
/// A minimal Debug Adapter Protocol server for one editor session. Translates DAP requests into
/// <see cref="DebugSession"/> calls and emits <c>stopped</c>/<c>continued</c>/<c>terminated</c>
/// events. One instance per connection; <see cref="Run"/> blocks until the client disconnects.
///
/// MVP command set: initialize, launch, configurationDone, setBreakpoints, threads, stackTrace,
/// scopes, variables, continue, next, stepIn, stepOut, pause, disconnect/terminate.
/// </summary>
public sealed class DapServer
{
    private const int ThreadId = 1;

    private readonly DapConnection _conn;
    private readonly IDebugHost _host;
    private readonly DebugSession _session = new();
    private readonly CancellationTokenSource _cts = new();

    private int _pendingYears = 100;
    private bool _runStarted;
    private bool _attachMode;
    private Thread? _worker;

    public DapServer(DapConnection conn, IDebugHost host)
    {
        _conn = conn;
        _host = host;

        _session.Stopped += info =>
            _conn.SendEvent("stopped", new JsonObject
            {
                ["reason"] = Reason(info.Reason),
                ["threadId"] = ThreadId,
                ["allThreadsStopped"] = true,
                ["line"] = info.Line,
            });
        _session.Continued += () =>
            _conn.SendEvent("continued", new JsonObject { ["threadId"] = ThreadId, ["allThreadsContinued"] = true });
    }

    private static string Reason(DebugSession.StopReason r) => r switch
    {
        DebugSession.StopReason.Breakpoint => "breakpoint",
        DebugSession.StopReason.Step => "step",
        DebugSession.StopReason.Pause => "pause",
        DebugSession.StopReason.Entry => "entry",
        _ => "step",
    };

    public void Run()
    {
        while (true)
        {
            JsonObject? msg;
            try { msg = _conn.Read(); }
            catch { break; }
            if (msg == null)
                break;

            if (msg["type"]?.GetValue<string>() != "request")
                continue;

            try { Dispatch(msg); }
            catch (Exception e) { _conn.SendResponse(msg, false, message: e.Message); }
        }

        _host.DetachSession(_session);
        _session.Terminate();
        _cts.Cancel();
    }

    private void Dispatch(JsonObject req)
    {
        var command = req["command"]?.GetValue<string>() ?? "";
        var args = req["arguments"] as JsonObject;

        switch (command)
        {
            case "initialize":
                _conn.SendResponse(req, true, new JsonObject
                {
                    ["supportsConfigurationDoneRequest"] = true,
                    ["supportsTerminateRequest"] = true,
                });
                _conn.SendEvent("initialized");
                break;

            case "launch":
                if (args?["years"] is { } y && int.TryParse(y.ToString(), out var years))
                    _pendingYears = years;
                if (args?["stopOnEntry"]?.GetValue<bool>() == true)
                    _session.SetStopOnEntry(true);
                _conn.SendResponse(req, true);
                break;

            case "attach":
                // Don't drive a run; install the session so web-UI-triggered runs hit breakpoints.
                _attachMode = true;
                _conn.SendResponse(req, true);
                break;

            case "setBreakpoints":
                _conn.SendResponse(req, true, HandleSetBreakpoints(args));
                break;

            case "configurationDone":
                _conn.SendResponse(req, true);
                if (_attachMode)
                    _host.AttachSession(_session);
                else
                    StartRun();
                break;

            case "threads":
                _conn.SendResponse(req, true, new JsonObject
                {
                    ["threads"] = new JsonArray { new JsonObject { ["id"] = ThreadId, ["name"] = "simulation" } },
                });
                break;

            case "stackTrace":
                _conn.SendResponse(req, true, HandleStackTrace());
                break;

            case "scopes":
                _conn.SendResponse(req, true, HandleScopes(args));
                break;

            case "variables":
                _conn.SendResponse(req, true, HandleVariables(args));
                break;

            // For resume requests the response MUST reach the client before the resulting `stopped`
            // event, or VS Code wedges (ignores the stop). Resuming wakes the worker thread, which can
            // emit `stopped` immediately — so always send the response first, then resume.
            case "continue":
                _conn.SendResponse(req, true, new JsonObject { ["allThreadsContinued"] = true });
                _session.Continue();
                break;

            case "next":
                _conn.SendResponse(req, true);
                _session.StepOver();
                break;

            case "stepIn":
                _conn.SendResponse(req, true);
                _session.StepIn();
                break;

            case "stepOut":
                _conn.SendResponse(req, true);
                _session.StepOut();
                break;

            case "pause":
                _session.Pause();
                _conn.SendResponse(req, true);
                break;

            case "disconnect":
            case "terminate":
                _host.DetachSession(_session);
                _session.Terminate();
                _conn.SendResponse(req, true);
                break;

            default:
                // Unknown/unsupported request: acknowledge so the client isn't left waiting.
                _conn.SendResponse(req, true);
                break;
        }
    }

    private void StartRun()
    {
        if (_runStarted)
            return;
        _runStarted = true;

        _worker = new Thread(() =>
        {
            try { _host.RunDebugged(_pendingYears, _session, _cts.Token); }
            catch (Exception e) { _conn.SendEvent("output", new JsonObject { ["category"] = "stderr", ["output"] = e + "\n" }); }
            finally { _conn.SendEvent("terminated"); }
        }) { IsBackground = true, Name = "moirai-debug-sim" };
        _worker.Start();
    }

    private JsonObject HandleSetBreakpoints(JsonObject? args)
    {
        var path = (args?["source"] as JsonObject)?["path"]?.GetValue<string>() ?? "";
        var requested = new List<int>();
        if (args?["breakpoints"] is JsonArray bps)
        {
            foreach (var bp in bps)
                if (bp?["line"] is { } l && int.TryParse(l.ToString(), out var ln))
                    requested.Add(ln);
        }

        // Snap each requested line to the nearest executable statement at or below it. A breakpoint
        // on a signature line (e.g. `function foo() {`), a blank line, or inside a multi-line
        // statement otherwise never matches an instruction's start line and silently never fires.
        var executable = ExecutableLines();
        var verified = new JsonArray();
        var resolved = new List<int>();
        foreach (var l in requested)
        {
            int snapped = NearestExecutable(executable, l);
            if (snapped > 0)
            {
                resolved.Add(snapped);
                verified.Add(new JsonObject { ["verified"] = true, ["line"] = snapped });
            }
            else
            {
                verified.Add(new JsonObject { ["verified"] = false, ["line"] = l });
            }
        }

        _session.SetBreakpoints(path, resolved);
        return new JsonObject { ["breakpoints"] = verified };
    }

    // 1-based executable lines (the engine records 0-based statement starts).
    private SortedSet<int> ExecutableLines()
    {
        var set = new SortedSet<int>();
        foreach (var line0 in _host.Database.DebugStatementLines)
            set.Add(line0 + 1);
        return set;
    }

    // Smallest executable line >= requested; if none above, the closest one at/below it.
    private static int NearestExecutable(SortedSet<int> executable, int requested)
    {
        if (executable.Count == 0)
            return requested; // no info: trust the client's line
        foreach (var e in executable)
            if (e >= requested)
                return e;
        return executable.Max;
    }

    private JsonObject HandleStackTrace()
    {
        var frames = new JsonArray();
        foreach (var f in _session.GetStack())
        {
            frames.Add(new JsonObject
            {
                ["id"] = f.Id,
                ["name"] = f.Name,
                ["line"] = f.Line,
                ["column"] = f.Column,
                ["source"] = new JsonObject
                {
                    ["name"] = Path.GetFileName(_host.ProgramPath),
                    ["path"] = _host.ProgramPath,
                },
            });
        }

        return new JsonObject { ["stackFrames"] = frames, ["totalFrames"] = frames.Count };
    }

    private JsonObject HandleScopes(JsonObject? args)
    {
        int frameId = args?["frameId"]?.GetValue<int>() ?? 0;
        return new JsonObject
        {
            ["scopes"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Locals",
                    ["variablesReference"] = _session.GetScopeReference(frameId),
                    ["expensive"] = false,
                },
                new JsonObject
                {
                    ["name"] = "World",
                    ["variablesReference"] = _session.GetWorldReference(),
                    ["expensive"] = false,
                },
            },
        };
    }

    private JsonObject HandleVariables(JsonObject? args)
    {
        int reference = args?["variablesReference"]?.GetValue<int>() ?? 0;
        var vars = new JsonArray();
        foreach (var v in _session.GetVariablesByReference(reference))
        {
            vars.Add(new JsonObject
            {
                ["name"] = v.Name,
                ["value"] = v.Value,
                // Non-zero => the client shows an expand arrow and re-requests with this reference.
                ["variablesReference"] = v.VariablesReference,
            });
        }

        return new JsonObject { ["variables"] = vars };
    }
}
