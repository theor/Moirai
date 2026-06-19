using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading;
using Moirai.Core;
using Moirai.DebugAdapter;
using Moirai.Parser;

namespace TestProject1;

/// <summary>
/// End-to-end test of the DAP server over a real loopback TCP socket: a client drives the full
/// handshake (initialize → setBreakpoints → configurationDone), the simulation runs under the
/// debug hook, and the client observes a <c>stopped</c> event with a correct stack and variables.
/// </summary>
public class DapServerIntegrationTests
{
    private const string Story = @"
entity Person {
    prop alive: bool
    prop age: number
}
@start
event create_time {
    create Time $t: 'time'
    set $t.year = 0
}
@frequency(1, EveryXYear, 1)
event spawn {
    create Person $p: 'p'
    set $p.alive = true
}";

    private sealed class FakeHost : IDebugHost
    {
        public Database Database { get; }
        public string ProgramPath { get; }
        private readonly int _years;

        public FakeHost(Database db, string path, int years)
        {
            Database = db;
            ProgramPath = path;
            _years = years;
        }

        public void RunDebugged(int years, DebugSession session, CancellationToken ct)
        {
            Database.DebugHook = session;
            try { Database.Ctx.PassYears(_years, ct, null, true); }
            finally { Database.DebugHook = null; }
        }
    }

    private static int LineOf(string text, string needle)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains(needle))
                return i + 1;
        return -1;
    }

    [Test]
    public void FullHandshakeStopsAtBreakpoint()
    {
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        db.History = new();
        db.Init();

        int bpLine = LineOf(Story, "set $p.alive = true");

        var listener = new DapListener(new FakeHost(db, "story.sg", 2), 0);
        listener.Start();

        using var tcp = new TcpClient();
        Assert.That(tcp.ConnectAsync("127.0.0.1", listener.Port).Wait(5000), Is.True, "could not connect");
        var stream = tcp.GetStream();
        stream.ReadTimeout = 5000;
        var conn = new DapConnection(stream, stream);

        // initialize -> expect response + 'initialized' event.
        conn.SendRequest("initialize", new JsonObject { ["adapterID"] = "moirai" });
        Assert.That(WaitFor(conn, m => m["type"]?.GetValue<string>() == "event"
                                       && m["event"]?.GetValue<string>() == "initialized"),
            Is.Not.Null, "no initialized event");

        // setBreakpoints on the spawn line.
        conn.SendRequest("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = "story.sg" },
            ["breakpoints"] = new JsonArray { new JsonObject { ["line"] = bpLine } },
        });
        var bpResp = WaitFor(conn, m => m["type"]?.GetValue<string>() == "response"
                                        && m["command"]?.GetValue<string>() == "setBreakpoints");
        Assert.That(bpResp, Is.Not.Null);

        conn.SendRequest("launch", new JsonObject { ["years"] = 2 });
        conn.SendRequest("configurationDone");

        // Expect a stopped event at the breakpoint.
        var stopped = WaitFor(conn, m => m["type"]?.GetValue<string>() == "event"
                                         && m["event"]?.GetValue<string>() == "stopped");
        Assert.That(stopped, Is.Not.Null, "never stopped at the breakpoint");
        var stopBody = stopped!["body"] as JsonObject;
        Assert.That(stopBody!["reason"]!.GetValue<string>(), Is.EqualTo("breakpoint"));
        Assert.That(stopBody["line"]!.GetValue<int>(), Is.EqualTo(bpLine));

        // stackTrace -> top frame is `spawn` at the breakpoint line.
        conn.SendRequest("stackTrace", new JsonObject { ["threadId"] = 1 });
        var stack = WaitFor(conn, m => m["command"]?.GetValue<string>() == "stackTrace");
        var frames = (JsonArray)((JsonObject)stack!["body"]!)["stackFrames"]!;
        Assert.That(frames.Count, Is.GreaterThan(0));
        var top = (JsonObject)frames[0]!;
        Assert.That(top["name"]!.GetValue<string>(), Is.EqualTo("spawn"));
        Assert.That(top["line"]!.GetValue<int>(), Is.EqualTo(bpLine));

        // scopes -> variables: $p is present.
        conn.SendRequest("scopes", new JsonObject { ["frameId"] = top["id"]!.GetValue<int>() });
        var scopes = WaitFor(conn, m => m["command"]?.GetValue<string>() == "scopes");
        int varRef = ((JsonObject)((JsonArray)((JsonObject)scopes!["body"]!)["scopes"]!)[0]!)["variablesReference"]!.GetValue<int>();
        conn.SendRequest("variables", new JsonObject { ["variablesReference"] = varRef });
        var vars = WaitFor(conn, m => m["command"]?.GetValue<string>() == "variables");
        var varArray = (JsonArray)((JsonObject)vars!["body"]!)["variables"]!;
        Assert.That(varArray.Select(v => ((JsonObject)v!)["name"]!.GetValue<string>()), Does.Contain("$p"));

        // Run to completion: the breakpoint fires again each subsequent year, so keep continuing.
        conn.SendRequest("continue", new JsonObject { ["threadId"] = 1 });
        var terminated = WaitFor(conn, m =>
        {
            var ev = m["event"]?.GetValue<string>();
            if (ev == "stopped")
                conn.SendRequest("continue", new JsonObject { ["threadId"] = 1 });
            return ev == "terminated";
        }, 8000);
        Assert.That(terminated, Is.Not.Null, "never terminated");

        conn.SendRequest("disconnect");
    }

    // Read messages until predicate matches or the timeout/stream closes.
    private static JsonObject? WaitFor(DapConnection conn, Func<JsonObject, bool> predicate, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            JsonObject? m;
            try { m = conn.Read(); }
            catch { return null; }
            if (m == null) return null;
            if (predicate(m)) return m;
        }

        return null;
    }
}
