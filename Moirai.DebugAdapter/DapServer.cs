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

            case "setBreakpoints":
                _conn.SendResponse(req, true, HandleSetBreakpoints(args));
                break;

            case "configurationDone":
                _conn.SendResponse(req, true);
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

            case "continue":
                _session.Continue();
                _conn.SendResponse(req, true, new JsonObject { ["allThreadsContinued"] = true });
                break;

            case "next":
                _session.StepOver();
                _conn.SendResponse(req, true);
                break;

            case "stepIn":
                _session.StepIn();
                _conn.SendResponse(req, true);
                break;

            case "stepOut":
                _session.StepOut();
                _conn.SendResponse(req, true);
                break;

            case "pause":
                _session.Pause();
                _conn.SendResponse(req, true);
                break;

            case "disconnect":
            case "terminate":
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
        var lines = new List<int>();
        if (args?["breakpoints"] is JsonArray bps)
        {
            foreach (var bp in bps)
                if (bp?["line"] is { } l && int.TryParse(l.ToString(), out var ln))
                    lines.Add(ln);
        }

        var accepted = new HashSet<int>(_session.SetBreakpoints(path, lines));
        var verified = new JsonArray();
        foreach (var l in lines)
            verified.Add(new JsonObject { ["verified"] = accepted.Contains(l), ["line"] = l });
        return new JsonObject { ["breakpoints"] = verified };
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
        // Encode the frame id into the variablesReference (1-based; 0 means "no variables").
        return new JsonObject
        {
            ["scopes"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Locals",
                    ["variablesReference"] = frameId + 1,
                    ["expensive"] = false,
                },
            },
        };
    }

    private JsonObject HandleVariables(JsonObject? args)
    {
        int reference = args?["variablesReference"]?.GetValue<int>() ?? 0;
        var vars = new JsonArray();
        if (reference > 0)
        {
            int frameId = reference - 1;
            foreach (var v in _session.GetVariables(frameId))
            {
                vars.Add(new JsonObject
                {
                    ["name"] = v.Name,
                    ["value"] = v.Value,
                    ["variablesReference"] = 0,
                });
            }
        }

        return new JsonObject { ["variables"] = vars };
    }
}
