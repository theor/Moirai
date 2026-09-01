using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Moirai.Core;

namespace TestProject1;

/// <summary>
/// A story running under a <see cref="DebugSession"/> on a worker thread — the shape every
/// step-through test needs, because that is how a debug adapter drives the engine: the simulation
/// blocks inside <c>Suspend</c> while the test inspects it from the outside.
///
/// <para>It exists because that handshake is easy to get subtly wrong by hand, and the way it goes
/// wrong is a test that passes on a quiet machine and fails on a busy CI runner. Two rules are baked
/// in here so no test has to remember them.</para>
///
/// <para><b>Breakpoints are installed before the worker starts.</b> They are an argument to
/// <see cref="Start"/> rather than something the caller sets afterwards, because a story runs its
/// first year in microseconds: a breakpoint set after the thread is already going is a race, and the
/// run wins it whenever the runner is busy elsewhere. The test then waits out its whole timeout for a
/// stop that can no longer happen. That is not hypothetical — it is what
/// <c>StepIntoFunction</c> did, passing locally for months and failing on GitHub Actions.</para>
///
/// <para><b>Nothing gives up on the worker for being slow.</b> The waits below are bounded only so a
/// broken test fails instead of hanging; the bound is deliberately far larger than any real wait,
/// because the only thing a tight one buys is a suite that cries wolf.</para>
/// </summary>
internal sealed class DebugRun : IDisposable
{
    /// <summary>
    /// The ceiling on any wait for the worker. Reached only when something is genuinely stuck — a
    /// breakpoint is hit in microseconds — so it costs nothing to be generous.
    /// </summary>
    public const int TimeoutMs = 30_000;

    public DebugSession Session { get; }

    /// <summary>The lines the session actually accepted breakpoints on, as a debug adapter reports them.</summary>
    public int[] AcceptedBreakpoints { get; private set; } = Array.Empty<int>();

    /// <summary>Every stop the run has reported, in order.</summary>
    public BlockingCollection<DebugSession.StopInfo> Stops { get; } = new();

    private readonly ManualResetEventSlim _done = new(false);
    private Exception? _workerError;

    private DebugRun(DebugSession session) => Session = session;

    /// <summary>
    /// Init the world, arm the breakpoints, and start simulating on a background thread. The
    /// <c>@start</c> events run during <c>Init</c>, before any breakpoint exists, so they cannot stop.
    /// </summary>
    public static DebugRun Start(Database db, int years, string source, params int[] breakpointLines)
    {
        var session = new DebugSession();
        db.History = new History();
        db.DebugHook = session;
        db.Init();

        var run = new DebugRun(session);
        session.Stopped += s => run.Stops.Add(s);

        // Before the thread starts. See the class comment — this ordering is the whole point.
        if (breakpointLines.Length > 0)
            run.AcceptedBreakpoints = session.SetBreakpoints(source, breakpointLines);

        new Thread(() =>
        {
            try
            {
                db.Ctx.PassYears(years, true);
            }
            catch (Exception e)
            {
                run._workerError = e;
            }
            finally
            {
                run._done.Set();
            }
        }) { IsBackground = true, Name = "debug-run" }.Start();

        return run;
    }

    /// <summary>
    /// The next stop, or a failure naming what was expected. A worker exception is reported in
    /// preference to the timeout: the thread dying is the reason no stop arrived, and saying
    /// "expected True, but was False" instead of that has cost real debugging time.
    /// </summary>
    public DebugSession.StopInfo NextStop(string expectation)
    {
        if (Stops.TryTake(out var info, TimeoutMs))
            return info;

        Rethrow();
        Assert.Fail($"no stop within {TimeoutMs} ms: {expectation}");
        return default!;
    }

    /// <summary>
    /// Let the run finish, releasing it at every stop along the way.
    ///
    /// <para><see cref="DebugSession.Continue"/> is called on a tick rather than only in answer to a
    /// stop, and that is safe by construction: <c>Suspend</c> resets its gate <em>before</em> raising
    /// <c>Stopped</c>, so an early continue cannot skip a stop that has not happened yet. It is also
    /// what makes this immune to a descheduled worker — the loop it replaces waited a second for the
    /// next stop and broke out if none came, leaving the simulation suspended for good and failing on
    /// the join a few lines later.</para>
    /// </summary>
    public void Drain()
    {
        var clock = Stopwatch.StartNew();
        while (!_done.Wait(20))
        {
            Session.Continue();
            if (clock.ElapsedMilliseconds > TimeoutMs)
                Assert.Fail($"the simulation thread did not finish within {TimeoutMs} ms");
        }

        Rethrow();
    }

    /// <summary>Release the worker whatever state it is in, so a failed assertion leaves nothing suspended.</summary>
    public void Dispose()
    {
        Session.Terminate();
        _done.Wait(TimeoutMs);
    }

    private void Rethrow()
    {
        if (_workerError is { } e)
            Assert.Fail($"the simulation thread threw: {e}");
    }
}
