using System.Diagnostics;
using System.Threading.Channels;
using Moirai.Api;
using Moirai.Core;

namespace MoiraiWebServer.Hubs;

using Microsoft.AspNetCore.SignalR;

/// <summary>
/// The SignalR transport for a <see cref="WorldSession"/>. All the behaviour lives in the session; this
/// class is only two things: the wire names the client calls, and the lock that makes a mutable world
/// safe to share between connections.
///
/// <para><b>State is static and there is one world.</b> This is a single-tenant server by design — the
/// world is the point of the process, not a per-user resource — so every connection sees the same
/// session, guarded by one non-reentrant <see cref="SemaphoreSlim"/>. The timeouts differ per method on
/// purpose: a read that cannot get the lock returns empty rather than making the UI wait behind a
/// running simulation, while a write waits indefinitely because dropping it would lose work.</para>
/// </summary>
public class ChatHub : Hub
{
    private static WorldSession? _session;

    // A DAP client attached in "attach" mode: installed as the engine's debug hook so that
    // runs triggered from the web UI (PassYears/RunAction) hit its breakpoints. Survives reloads.
    private static DebugSession? _attachedSession;

    private static readonly SemaphoreSlim Mutex = new(1, 1);

    public ChatHub()
    {
        if (_session == null)
            GetOrCreateSession();
    }

    // Caller must hold Mutex, or be certain no one else can be running.
    private static WorldSession CreateSessionLocked()
    {
        var options = Program.OptionsInstance;
        // A factory, not a snapshot: Reset re-reads the file, which is what makes hot reload work.
        _session = new WorldSession(() => File.ReadAllText(options.InputFile), options.Seed, options.Profile)
        {
            DebugHook = _attachedSession,
        };
        return _session;
    }

    // ---- lifecycle -------------------------------------------------------

    public long Reset()
    {
        Mutex.Wait();
        try
        {
            return _session!.Reset();
        }
        finally
        {
            Mutex.Release();
        }
    }

    /// <summary>The seed the current world was built with.</summary>
    public ulong GetSeed() => _session!.GetSeed();

    /// <summary>Rebuild the world from a different seed. Returns the year of the fresh world.</summary>
    public long Reseed(ulong seed)
    {
        Mutex.Wait();
        try
        {
            return _session!.Reseed(seed);
        }
        finally
        {
            Mutex.Release();
        }
    }

    /// <summary>
    /// The story file changed on disk. Only flags it: the rebuild happens on the next tick of the record
    /// feed, on a thread that is allowed to touch the world.
    /// </summary>
    public static void ReloadRequested() => _session?.RequestReload(_session.Year);

    // ---- debug adapter bridge --------------------------------------------

    /// <summary>Install a DAP session as the persistent debug hook (attach mode).</summary>
    public static void AttachSession(DebugSession session)
    {
        _attachedSession = session;
        GetOrCreateSession().DebugHook = session;
    }

    public static void DetachSession(DebugSession session)
    {
        // Terminate first: a thread paused at a breakpoint is holding Mutex, and only resuming it will
        // give the lock back.
        if (!ReferenceEquals(_attachedSession, session))
            return;
        _attachedSession = null;
        if (_session != null)
            _session.DebugHook = null;
    }

    /// <summary>
    /// The live world, without taking the lock — deliberately. The debugger's protocol thread reads
    /// frames and variables while a paused run holds the mutex, so waiting for it here would deadlock
    /// continue/step.
    /// </summary>
    public static Database? CurrentDb => _session?.Database;

    /// <summary>Get the shared world, creating it from the input file if no client has yet.</summary>
    public static Database GetOrCreateDb() => GetOrCreateSession().Database;

    private static WorldSession GetOrCreateSession()
    {
        Mutex.Wait();
        try
        {
            return _session ?? CreateSessionLocked();
        }
        finally
        {
            Mutex.Release();
        }
    }

    /// <summary>
    /// Run a debugged simulation: install <paramref name="session"/> as the engine's debug hook and
    /// pass <paramref name="years"/> years under the shared mutex (so it does not race other clients).
    /// Called by the debug adapter on a worker thread; blocks until the pass completes.
    /// </summary>
    public static void RunDebugged(int years, DebugSession session, CancellationToken ct)
    {
        Mutex.Wait();
        try
        {
            var s = _session ?? CreateSessionLocked();
            s.DebugHook = session;
            try
            {
                s.PassYears(years, null, ct);
            }
            finally
            {
                s.DebugHook = _attachedSession;
            }
        }
        finally
        {
            Mutex.Release();
        }
    }

    // ---- simulation ------------------------------------------------------

    public ChannelReader<int> PassYears(int years)
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        // A short wait, then give up: a pass already running means the answer to "pass more years" is
        // "not now", and an empty stream is how the client hears that.
        if (!Mutex.Wait(100))
        {
            channel.Writer.Complete();
            return channel.Reader;
        }

        int tens = 0;
        IProgress<int> p = new Progress<int>(i =>
        {
            if (i / 10 > tens)
            {
                tens = i / 10;
                channel.Writer.TryWrite((int)(100 * i / (float)years));
            }
        });
        Task.Factory.StartNew(() =>
        {
            try
            {
                _session!.PassYears(years, p);
                channel.Writer.Complete();
            }
            finally
            {
                Mutex.Release();
            }
        });

        return channel.Reader;
    }

    public void Save()
    {
        Mutex.Wait();
        try
        {
            _session!.Save();
        }
        finally
        {
            Mutex.Release();
        }
    }

    public void RunAction(int actionId)
    {
        Mutex.Wait();
        try
        {
            _session!.RunAction(actionId);
        }
        finally
        {
            Mutex.Release();
        }
    }

    // ---- queries ---------------------------------------------------------

    public async Task<ClientData> GetClientData()
    {
        await Mutex.WaitAsync();
        try
        {
            return _session!.GetClientData();
        }
        finally
        {
            Mutex.Release();
        }
    }

    public Biography GetBiography(uint eid)
    {
        Mutex.Wait();
        try
        {
            return _session!.GetBiography(eid);
        }
        finally
        {
            Mutex.Release();
        }
    }

    public WorldOverview GetWorldOverview()
    {
        Mutex.Wait();
        try
        {
            return _session!.GetWorldOverview();
        }
        finally
        {
            Mutex.Release();
        }
    }

    public TimeSeries GetPropertySeries(int typeId, string propertyName)
    {
        Mutex.Wait();
        try
        {
            return _session!.GetPropertySeries(typeId, propertyName);
        }
        finally
        {
            Mutex.Release();
        }
    }

    public RuleCoverageReport GetRuleCoverage()
    {
        Mutex.Wait();
        try
        {
            return _session!.GetRuleCoverage();
        }
        finally
        {
            Mutex.Release();
        }
    }

    public async Task<QueryResult> Query(string q)
    {
        await Mutex.WaitAsync();
        try
        {
            return _session!.Query(q);
        }
        finally
        {
            Mutex.Release();
        }
    }

    public async Task<List<FamilyTreeNode>> GetFamilyTree(uint eid, int maxDepth)
    {
        if (!await Mutex.WaitAsync(500))
            return new List<FamilyTreeNode>();
        try
        {
            return _session!.GetFamilyTree(eid, maxDepth);
        }
        finally
        {
            Mutex.Release();
        }
    }

    public IList<EntityPropertyDisplay> GetEntityDetails(uint eid)
    {
        if (!Mutex.Wait(500))
            return new List<EntityPropertyDisplay>();
        try
        {
            return _session!.GetEntityDetails(eid);
        }
        finally
        {
            Mutex.Release();
        }
    }

    public int GetChangesetsCount()
    {
        Mutex.Wait();
        try
        {
            return _session!.GetChangesetsCount();
        }
        finally
        {
            Mutex.Release();
        }
    }

    public List<EntityChangeDisplay> GetChangesets(int start, int count)
    {
        Mutex.Wait();
        try
        {
            return _session!.GetChangesets(start, count);
        }
        finally
        {
            Mutex.Release();
        }
    }

    public List<EntityChangeDisplay> GetEntityChangesets(uint eid)
    {
        Mutex.Wait();
        try
        {
            return _session!.GetEntityChangesets(eid);
        }
        finally
        {
            Mutex.Release();
        }
    }

    // ---- the record feed -------------------------------------------------

    /// <summary>
    /// The one server-to-client channel: new records, a year heartbeat, and reset notices. The client
    /// subscribes once at startup and follows the world through it.
    /// </summary>
    public ChannelReader<Message> Stream(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<Message>();

        // Not awaited: the client needs the reader back now, not after the last item is written.
        _ = WriteItemsAsync(channel.Writer, cancellationToken);

        return channel.Reader;
    }

    private async Task WriteItemsAsync(
        ChannelWriter<Message> writer,
        CancellationToken cancellationToken)
    {
        Exception? localException = null;
        try
        {
            Debug.WriteLine("Stream");
            int cursor = 0;
            while (true)
            {
                await Mutex.WaitAsync(cancellationToken);
                List<Message> batch;
                try
                {
                    batch = _session!.DrainFeed(cursor, out cursor);
                }
                finally
                {
                    Mutex.Release();
                }

                // Written outside the lock: a slow client must not be able to hold the world still.
                foreach (var m in batch)
                    await writer.WriteAsync(m, cancellationToken);

                await Task.Delay(500, cancellationToken);
            }
        }
        catch (Exception e)
        {
            localException = e;
        }
        finally
        {
            writer.Complete(localException);
        }
    }
}
