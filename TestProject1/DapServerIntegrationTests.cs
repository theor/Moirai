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

        // Attach: install the hook persistently; a "web UI" run (driven by the test) then hits it.
        public void AttachSession(DebugSession session) => Database.DebugHook = session;

        public void DetachSession(DebugSession session)
        {
            session.Terminate();
            if (Database.DebugHook == session) Database.DebugHook = null;
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

    private const string FuncStory = @"
entity Country {
    prop founded: number
}
function make_country() {
    create Country $c: 'c'
    record('made')
}
@start
event setup {
    create Time $t: 'time'
    set $t.year = 0
}
@frequency(1, EveryXYear, 1)
event run {
    call(make_country, 1)
}";

    [Test]
    public void BreakpointInsideFunctionHitsOverWire()
    {
        var db = StoryParser.Parse(FuncStory, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        db.History = new();
        db.Init();

        int bpLine = LineOf(FuncStory, "record('made')");

        var listener = new DapListener(new FakeHost(db, "story.sg", 1), 0);
        listener.Start();

        using var tcp = new TcpClient();
        Assert.That(tcp.ConnectAsync("127.0.0.1", listener.Port).Wait(5000), Is.True);
        var stream = tcp.GetStream();
        stream.ReadTimeout = 5000;
        var conn = new DapConnection(stream, stream);

        conn.SendRequest("initialize", new JsonObject { ["adapterID"] = "moirai" });
        WaitFor(conn, m => m["event"]?.GetValue<string>() == "initialized");
        conn.SendRequest("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = "story.sg" },
            ["breakpoints"] = new JsonArray { new JsonObject { ["line"] = bpLine } },
        });
        WaitFor(conn, m => m["command"]?.GetValue<string>() == "setBreakpoints");
        conn.SendRequest("launch", new JsonObject { ["years"] = 1 });
        conn.SendRequest("configurationDone");

        var stopped = WaitFor(conn, m => m["event"]?.GetValue<string>() == "stopped");
        Assert.That(stopped, Is.Not.Null, "never stopped inside the function");
        Assert.That(((JsonObject)stopped!["body"]!)["line"]!.GetValue<int>(), Is.EqualTo(bpLine));

        conn.SendRequest("stackTrace", new JsonObject { ["threadId"] = 1 });
        var stack = WaitFor(conn, m => m["command"]?.GetValue<string>() == "stackTrace");
        var frames = (JsonArray)((JsonObject)stack!["body"]!)["stackFrames"]!;
        Assert.That(((JsonObject)frames[0]!)["name"]!.GetValue<string>(), Is.EqualTo("make_country"));

        conn.SendRequest("continue", new JsonObject { ["threadId"] = 1 });
        WaitFor(conn, m =>
        {
            if (m["event"]?.GetValue<string>() == "stopped")
                conn.SendRequest("continue", new JsonObject { ["threadId"] = 1 });
            return m["event"]?.GetValue<string>() == "terminated";
        }, 8000);
        conn.SendRequest("disconnect");
    }

    [Test]
    public void StepResponsePrecedesStoppedEvent()
    {
        // Regression: the response to a resume request must reach the client before the resulting
        // `stopped` event, or VS Code intermittently wedges (continue/step "stops responding").
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        db.History = new();
        db.Init();

        int createLine = LineOf(Story, "create Person $p");
        var listener = new DapListener(new FakeHost(db, "story.sg", 1), 0);
        listener.Start();

        using var tcp = new TcpClient();
        Assert.That(tcp.ConnectAsync("127.0.0.1", listener.Port).Wait(5000), Is.True);
        var stream = tcp.GetStream();
        stream.ReadTimeout = 5000;
        var conn = new DapConnection(stream, stream);

        conn.SendRequest("initialize", new JsonObject { ["adapterID"] = "moirai" });
        WaitFor(conn, m => m["event"]?.GetValue<string>() == "initialized");
        conn.SendRequest("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = "story.sg" },
            ["breakpoints"] = new JsonArray { new JsonObject { ["line"] = createLine } },
        });
        WaitFor(conn, m => m["command"]?.GetValue<string>() == "setBreakpoints");
        conn.SendRequest("launch", new JsonObject { ["years"] = 1 });
        conn.SendRequest("configurationDone");
        WaitFor(conn, m => m["event"]?.GetValue<string>() == "stopped");

        // Step over; read messages strictly in order and confirm the `next` response comes first.
        conn.SendRequest("next");
        bool sawNextResponse = false;
        for (int i = 0; i < 10; i++)
        {
            var m = conn.Read();
            Assert.That(m, Is.Not.Null);
            if (m!["type"]?.GetValue<string>() == "response" && m["command"]?.GetValue<string>() == "next")
                sawNextResponse = true;
            if (m["event"]?.GetValue<string>() == "stopped")
            {
                Assert.That(sawNextResponse, Is.True, "stopped event arrived before the 'next' response");
                break;
            }
        }
        Assert.That(sawNextResponse, Is.True, "never saw the 'next' response");

        stream.ReadTimeout = 500;
        conn.SendRequest("continue", new JsonObject { ["threadId"] = 1 });
        WaitFor(conn, m =>
        {
            if (m["event"]?.GetValue<string>() == "stopped")
                conn.SendRequest("continue", new JsonObject { ["threadId"] = 1 });
            return m["event"]?.GetValue<string>() == "terminated";
        }, 8000);
        conn.SendRequest("disconnect");
    }

    [Test]
    public void AttachHitsBreakpointFromExternalRun()
    {
        // Simulates: VS Code attaches, sets a breakpoint, then the user clicks "pass years" in the
        // web UI. The externally-driven run must hit the breakpoint via the attached session.
        var db = StoryParser.Parse(Story, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        db.History = new();
        db.Init();

        int bpLine = LineOf(Story, "set $p.alive = true");
        var listener = new DapListener(new FakeHost(db, "story.sg", 2), 0);
        listener.Start();

        using var tcp = new TcpClient();
        Assert.That(tcp.ConnectAsync("127.0.0.1", listener.Port).Wait(5000), Is.True);
        var stream = tcp.GetStream();
        stream.ReadTimeout = 5000;
        var conn = new DapConnection(stream, stream);

        conn.SendRequest("initialize", new JsonObject { ["adapterID"] = "moirai" });
        WaitFor(conn, m => m["event"]?.GetValue<string>() == "initialized");
        conn.SendRequest("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = "story.sg" },
            ["breakpoints"] = new JsonArray { new JsonObject { ["line"] = bpLine } },
        });
        WaitFor(conn, m => m["command"]?.GetValue<string>() == "setBreakpoints");

        // attach (not launch): no DAP-driven run starts.
        conn.SendRequest("attach", new JsonObject());
        conn.SendRequest("configurationDone");
        WaitFor(conn, m => m["command"]?.GetValue<string>() == "configurationDone");

        // Now the "web UI" drives a run on its own thread; the attached hook is already installed.
        var done = new ManualResetEventSlim(false);
        new Thread(() => { try { db.Ctx.PassYears(2, true); } finally { done.Set(); } })
            { IsBackground = true }.Start();

        var stopped = WaitFor(conn, m => m["event"]?.GetValue<string>() == "stopped");
        Assert.That(stopped, Is.Not.Null, "external run did not hit the attached breakpoint");
        Assert.That(((JsonObject)stopped!["body"]!)["line"]!.GetValue<int>(), Is.EqualTo(bpLine));

        conn.SendRequest("stackTrace", new JsonObject { ["threadId"] = 1 });
        var stack = WaitFor(conn, m => m["command"]?.GetValue<string>() == "stackTrace");
        Assert.That(((JsonObject)((JsonArray)((JsonObject)stack!["body"]!)["stackFrames"]!)[0]!)["name"]!.GetValue<string>(),
            Is.EqualTo("spawn"));

        // The breakpoint fires once per simulated year; continue past each until the run finishes.
        // (attach has no DAP "terminated" event, so we poll the external run's done signal.)
        conn.SendRequest("continue", new JsonObject { ["threadId"] = 1 });
        stream.ReadTimeout = 500;   // short, so a quiet socket doesn't block the drain loop
        while (!done.Wait(0))
        {
            bool wasStopped;
            try { wasStopped = conn.Read()?["event"]?.GetValue<string>() == "stopped"; }
            catch { wasStopped = false; }   // read timed out: no pending message
            if (wasStopped)
                conn.SendRequest("continue", new JsonObject { ["threadId"] = 1 });
        }
        Assert.That(done.Wait(5000), Is.True, "external run never finished");

        stream.ReadTimeout = 5000;
        conn.SendRequest("disconnect");
    }

    [Test]
    public void BreakpointOnSignatureLineSnapsToFunctionBody()
    {
        var db = StoryParser.Parse(FuncStory, out var errors);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
        db.History = new();
        db.Init();

        // User drops the breakpoint on the (non-executable) `function make_country() {` line.
        int sigLine = LineOf(FuncStory, "function make_country()");
        int firstBody = LineOf(FuncStory, "create Country $c");

        var listener = new DapListener(new FakeHost(db, "story.sg", 1), 0);
        listener.Start();

        using var tcp = new TcpClient();
        Assert.That(tcp.ConnectAsync("127.0.0.1", listener.Port).Wait(5000), Is.True);
        var stream = tcp.GetStream();
        stream.ReadTimeout = 5000;
        var conn = new DapConnection(stream, stream);

        conn.SendRequest("initialize", new JsonObject { ["adapterID"] = "moirai" });
        WaitFor(conn, m => m["event"]?.GetValue<string>() == "initialized");

        conn.SendRequest("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = "story.sg" },
            ["breakpoints"] = new JsonArray { new JsonObject { ["line"] = sigLine } },
        });
        var bpResp = WaitFor(conn, m => m["command"]?.GetValue<string>() == "setBreakpoints");
        // The adapter should report the breakpoint snapped down to the first body statement.
        var reported = (JsonObject)((JsonArray)((JsonObject)bpResp!["body"]!)["breakpoints"]!)[0]!;
        Assert.That(reported["verified"]!.GetValue<bool>(), Is.True);
        Assert.That(reported["line"]!.GetValue<int>(), Is.EqualTo(firstBody), "breakpoint should snap to the first body line");

        conn.SendRequest("launch", new JsonObject { ["years"] = 1 });
        conn.SendRequest("configurationDone");

        var stopped = WaitFor(conn, m => m["event"]?.GetValue<string>() == "stopped");
        Assert.That(stopped, Is.Not.Null, "never stopped after snapping the breakpoint into the function");
        Assert.That(((JsonObject)stopped!["body"]!)["line"]!.GetValue<int>(), Is.EqualTo(firstBody));

        conn.SendRequest("continue", new JsonObject { ["threadId"] = 1 });
        WaitFor(conn, m =>
        {
            if (m["event"]?.GetValue<string>() == "stopped")
                conn.SendRequest("continue", new JsonObject { ["threadId"] = 1 });
            return m["event"]?.GetValue<string>() == "terminated";
        }, 8000);
        conn.SendRequest("disconnect");
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
        var scopeArr = (JsonArray)((JsonObject)scopes!["body"]!)["scopes"]!;
        var scopeNames = scopeArr.Select(s => ((JsonObject)s!)["name"]!.GetValue<string>()).ToArray();
        Assert.That(scopeNames, Does.Contain("Locals"));
        Assert.That(scopeNames, Does.Contain("World"));

        // World scope expands to the year + entity counts.
        int worldRef = scopeArr.Select(s => (JsonObject)s!).First(s => s["name"]!.GetValue<string>() == "World")["variablesReference"]!.GetValue<int>();
        conn.SendRequest("variables", new JsonObject { ["variablesReference"] = worldRef });
        var worldVars = WaitFor(conn, m => m["command"]?.GetValue<string>() == "variables");
        var worldArr = (JsonArray)((JsonObject)worldVars!["body"]!)["variables"]!;
        Assert.That(worldArr.Select(v => ((JsonObject)v!)["name"]!.GetValue<string>()), Does.Contain("year"));

        int varRef = scopeArr.Select(s => (JsonObject)s!).First(s => s["name"]!.GetValue<string>() == "Locals")["variablesReference"]!.GetValue<int>();
        conn.SendRequest("variables", new JsonObject { ["variablesReference"] = varRef });
        var vars = WaitFor(conn, m => m["command"]?.GetValue<string>() == "variables");
        var varArray = (JsonArray)((JsonObject)vars!["body"]!)["variables"]!;
        Assert.That(varArray.Select(v => ((JsonObject)v!)["name"]!.GetValue<string>()), Does.Contain("$p"));

        // $p is an entity: it must be expandable, and expanding it yields its properties.
        var pVar = varArray.Select(v => (JsonObject)v!).First(v => v["name"]!.GetValue<string>() == "$p");
        int pRef = pVar["variablesReference"]!.GetValue<int>();
        Assert.That(pRef, Is.GreaterThan(0), "entity variable should be expandable");
        conn.SendRequest("variables", new JsonObject { ["variablesReference"] = pRef });
        var pProps = WaitFor(conn, m => m["command"]?.GetValue<string>() == "variables");
        var pPropArr = (JsonArray)((JsonObject)pProps!["body"]!)["variables"]!;
        Assert.That(pPropArr.Count, Is.GreaterThan(0), "expanding an entity should list its properties");

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
