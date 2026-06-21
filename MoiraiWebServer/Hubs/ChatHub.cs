using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using JetBrains.Annotations;
using Moirai.Core;
using Moirai.Parser;

namespace MoiraiWebServer.Hubs;

using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    private static Database? _db;
    private static bool _reset;
    private static bool _resetRequested;

    // A DAP client attached in "attach" mode: installed as the engine's debug hook so that
    // runs triggered from the web UI (PassYears/RunAction) hit its breakpoints. Survives reloads.
    private static Moirai.Core.DebugSession? _attachedSession;

    private static readonly SemaphoreSlim Mutex = new(1, 1);
    public ChatHub()
    {
        Debug.WriteLine("Ctor");
        
            if (_db == null)
            {
                Reset();
            }
       
    }

    public static void ReloadRequested()
    {
        _resetRequested = true;
        _targetYears = _db?.Ctx.Year;
    }

    public long Reset()
    {
        Mutex.Wait();
        try
        {
            ResetLocked();
        }
        finally
        {
            Mutex.Release();
        }

        return _db!.Ctx.Year;
    }

    // Rebuild the world from the input file. Caller must hold Mutex.
    private static void ResetLocked()
    {
        _db = StoryParser.Parse(File.ReadAllText(Program.OptionsInstance.InputFile), out List<StoryParser.Error> _);
        _db.History = new();
        _db.ProfilingEnabled = Program.OptionsInstance.Profile;
        _db.Init();
        _db.DebugHook = _attachedSession;   // keep an attached debugger hooked across reloads
        _reset = true;
    }

    /// <summary>Install a DAP session as the persistent debug hook (attach mode).</summary>
    public static void AttachSession(Moirai.Core.DebugSession session)
    {
        _attachedSession = session;
        var db = GetOrCreateDb();
        db.DebugHook = session;
    }

    /// <summary>Detach a DAP session: release any paused run, then clear the hook.</summary>
    public static void DetachSession(Moirai.Core.DebugSession session)
    {
        // Terminate first so a thread paused at a breakpoint resumes and releases the mutex;
        // the writes below are plain reference assignments (single-tenant server), no lock needed.
        session.Terminate();
        if (_attachedSession == session)
            _attachedSession = null;
        var db = _db;
        if (db != null && db.DebugHook == session)
            db.DebugHook = null;
    }

    /// <summary>
    /// The current world without acquiring the mutex (null if not built yet). Safe for the debug
    /// adapter's protocol thread to read while a run is paused holding the mutex — it only reads the
    /// reference and parse-time data (e.g. DebugStatementLines), never mutates.
    /// </summary>
    public static Database? CurrentDb => _db;

    /// <summary>Get the shared world, creating it from the input file if no client has yet.</summary>
    public static Database GetOrCreateDb()
    {
        Mutex.Wait();
        try
        {
            if (_db == null)
                ResetLocked();
            return _db!;
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
    public static void RunDebugged(int years, Moirai.Core.DebugSession session, CancellationToken ct)
    {
        Mutex.Wait();
        try
        {
            if (_db == null)
                ResetLocked();
            _db!.DebugHook = session;
            try
            {
                _db.Ctx.PassYears(years, ct, null, true);
            }
            finally
            {
                _db.DebugHook = null;
            }
        }
        finally
        {
            Mutex.Release();
        }
    }

    public ChannelReader<int> PassYears(int years)
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

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
                _db!.Ctx.PassYears(years, CancellationToken.None, p, true);
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
            _db!.Commit();
        }
        finally
        {
            Mutex.Release();
            
        }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public struct ClientData
    {
        public record ActionData(int Id, string Name);
        public record TypeData(int Id, string Name);

        public ActionData[] Actions;
        public TypeData[] Types;
    }

    public async Task<ClientData> GetClientData()
    {
        await Mutex.WaitAsync();
        try
        {
            return new ClientData
            {
                Actions = _db!.Actions.Select(a => new ClientData.ActionData(a.Id, a.Name)).ToArray(),
                Types = _db.Types.Skip(1).Select(a => new ClientData.TypeData((int)a.Id.Id, a.Name)).OrderBy(x => x.Name).ToArray(),
            };
        }
        finally
        {
            Mutex.Release();
        }
    }

    [UsedImplicitly]
    public record EntityPropertyDisplay(string Label, string Value);
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public struct FamilyTreeNode(uint id, string name, uint p1, uint p2) : IEquatable<FamilyTreeNode>
    {
        public uint Id { get; init; } = id;
        public string Name { get; init; } = name;
        public uint P1 { get; init; } = p1;
        public uint P2 { get; init; } = p2;

        public void Deconstruct(out uint id, out string name, out uint p1, out uint p2)
        {
            id = this.Id;
            name = this.Name;
            p1 = this.P1;
            p2 = this.P2;
        }

        public bool Equals(FamilyTreeNode other)
        {
            return Id == other.Id;
        }

        public override bool Equals(object? obj)
        {
            return obj is FamilyTreeNode other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)Id;
        }

        public static bool operator ==(FamilyTreeNode left, FamilyTreeNode right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(FamilyTreeNode left, FamilyTreeNode right)
        {
            return !left.Equals(right);
        }
    }

    private static List<EntityId> results = new();
    private static long? _targetYears;

    [UsedImplicitly]
    public record EntityChangeDisplay(EntityId Id, long Year, string ActionName, IList<EntityPropertyDisplay> Changes);

    private IList<EntityPropertyDisplay> GetChangeDetails(Changeset.Changed c)
    {
            if (c.Prev.Id.IsNull) // new entity
            {
                return c.New.Properties.Where(p => p.Id.IsValid)
                    .Select(p => new EntityPropertyDisplay(_db!.GetPropertyName(p.Id), PrintValue(p.Id, p.Value)))
                    .ToList();
            }

            return c.Prev.Properties.Where(p => p.Id.IsValid)
                .Select(p =>
                {
                    var p1 = c.New.GetProperty(p.Id);
                    return new EntityPropertyDisplay(_db!.GetPropertyName(p.Id),
                        PrintValue(p.Id, p.Value) + " -> " + PrintValue(p.Id, p1));
                }).ToList();
    }

    public struct QueryResult
    {
        public string? Sql;
        public Result[] Results;
        public string[] Errors;
        public string Query;
    }
    public struct Result
    {
        public EntityId Eid;
        public IList<EntityPropertyDisplay> Properties;
    }
    public async Task<QueryResult> Query(string q)
    {
        await Mutex.WaitAsync();
        try
        {
            string? sql = null;
            try
            {
                AstVisitor v = new AstVisitor(_db!, null!);
                var e = StoryParser.ParseExpr(v, q, 0, 0, out var errors);
                if (errors.Any())
                    return new QueryResult { Errors = errors.Select(error => error.ToString()).ToArray() };
                if (e is AssignPick pick)
                {
                    _db!.FindAll(pick.EntityType, pick.Value, pick.VariableIndex, ref results, out sql);
                    return new QueryResult
                    {
                        Sql = sql,
                        Query = JsonSerializer.Serialize(e, e.GetType(), new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            IncludeFields = true, IgnoreReadOnlyFields = false,IgnoreReadOnlyProperties = false, DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                        }),
                        Results = results.Select(eid => new Result
                        {
                            Eid = eid, Properties = EntityPropertyDisplays(eid.Id),
                        }).ToArray()
                    };
                }
                return  new QueryResult() { Errors = new[]{ "Instruction unsuited for query: " + e!.GetType() } };
            }
            catch (Exception e)
            {
                return  new QueryResult() { Sql = sql, Errors = new[]{ e.ToString() } };
            }
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
            var eventTrigger = _db!.Actions.FirstOrDefault(a => a.Id == actionId);
            if(eventTrigger != null)
                _db.RunAction(eventTrigger);
        }
        finally
        {
            Mutex.Release();
        }
    }

    private IList<EntityChangeDisplay> GetChangesetDetails(Changeset cs)
    {
        Mutex.Wait();
        try
        {
            return cs.Changes.Select(x => new EntityChangeDisplay(x.New.Id, cs.Year, cs.ActionName, GetChangeDetails(x))).ToList();
        }
        finally
        {
            Mutex.Release();
        }
    }

    public async Task<List<FamilyTreeNode>> GetFamilyTree(uint eid, int maxDepth)
    {
        HashSet<FamilyTreeNode> nodes = new();
        if(!await Mutex.WaitAsync(500))
            return new List<FamilyTreeNode>();
        try
        {
            var prop1 = _db!.GetPropertyId("Person", "parent1");
            var prop2 = _db.GetPropertyId("Person", "parent2");
            Queue<(EntityId id, int depth)> queue = new();
            queue.Enqueue((new(eid), 0));
            while (queue.TryDequeue(out var item))
            {
                if(!_db.TryGetEntity(item.id, out Entity e))
                    continue;
                var node = new FamilyTreeNode(e.Id.Id, 
                    e.TryGetProperty( Database.PropName, out var name) ? name.Value! : e.Id.ToString(),
                    item.depth >= maxDepth ? 0 : e.TryGetProperty(prop1, out var p1) ? p1.Id.Id : 0,
                    item.depth >= maxDepth ? 0 : e.TryGetProperty(prop2, out var p2) ? p2.Id.Id : 0
                    );
                if(node.P1 != 0)
                    queue.Enqueue((new(node.P1), item.depth+1));
                if(node.P2 != 0)
                    queue.Enqueue((new(node.P2), item.depth+1));
                nodes.Add(node);
            }

            var personType = _db.GetEntityType("Person");
            _db.FindAll(personType.Id, 
                new BinaryOperator(BinaryOperator.Operator.Or,
                    new BinaryOperator(BinaryOperator.Operator.Equals, new PropertyPath(-1, personType.RefType, prop1), new Literal(new EntityId(eid))),
                    new BinaryOperator(BinaryOperator.Operator.Equals, new PropertyPath(-1, personType.RefType,prop2), new Literal(new EntityId(eid)))
                    ), -1, ref results);
            foreach (var id in results)
            {
                queue.Enqueue((id, 0));

            }
            while (queue.TryDequeue(out var item))
            {
                if(!_db.TryGetEntity(item.id, out Entity e))
                    continue;
                var p1Id = item.depth >= maxDepth ? 0 : e.TryGetProperty(prop1, out var p1) ? p1.Id.Id : 0;
                var p2Id = item.depth >= maxDepth ? 0 : e.TryGetProperty(prop2, out var p2) ? p2.Id.Id : 0;
                var node = new FamilyTreeNode(e.Id.Id, 
                    e.TryGetProperty( Database.PropName, out var name) ? name.Value! : e.Id.ToString(),
                    p1Id,
                    p2Id
                );
                if(p1Id != 0)
                    nodes.Add(new(p1Id, "A", 0, 0));
                if(p2Id != 0)
                    nodes.Add(new(p2Id, "B", 0, 0));
                nodes.Add(node);
            }
        }
        finally
        {
            Mutex.Release();
        }
        return nodes.ToList();
    }
    public IList<EntityPropertyDisplay> GetEntityDetails(uint eid)
    {
        if(!Mutex.Wait(500))
            return new List<EntityPropertyDisplay>();
        try
        {
            return EntityPropertyDisplays(eid);
        }
        finally
        {
            Mutex.Release();
        }
    }

    private static IList<EntityPropertyDisplay> EntityPropertyDisplays(uint eid)
    {
        if (!_db!.TryGetEntity(new EntityId(eid), out var e))
        {
            return ImmutableList<EntityPropertyDisplay>.Empty;
        }
        var details = e.Properties.Where(p => p.Id.IsValid)
            .Select(p => new EntityPropertyDisplay(
                _db.GetPropertyName(p.Id),
                PrintValue(p.Id, p.Value))).ToList();
        var t = _db.GetEntityType(e.Type);
        details.Insert(0,new EntityPropertyDisplay("Type", t.Name));
        foreach (var display in t.Attributes)
        {
            using var _ = _db.Ctx.RunScope(false);
            _db.Ctx.SetArgument(display.VarIndex, e.Id);
            _db.FindAll(display.ReferencedType.Id, display.Value, display.OtherVarIndex, ref results);
            foreach (var id in results)
            {
                if (!_db.TryGetEntity(id, out var ee)) continue;
                
                
                _db.Ctx.SetArgument(display.OtherVarIndex, id);
                details.Add(new EntityPropertyDisplay(display.Label,
                    $"<{ee.Id}>{(_db.GetProperty(ee.Id, Database.PropName, out var val) ? val.Value : ee.Id)}</>" +
                    (display.ItemDisplay == null ? "" : _db.Printer.Format(display.ItemDisplay, _db, true))));
            }
        }

        return details;
        
    }

    private static string PrintValue(PropertyId propertyId, PropertyValue propertyValue)
    {
        var print = _db!.Printer.Print(propertyValue);
        string value;
        if (_db.GetPropertyType(propertyId, out var type) && type.IsRefType)
        {
            value = propertyValue.Id.IsNull
                ? "null"
                : $"<{print}>{(_db.GetProperty(propertyValue.Id, Database.PropName, out var val) ? val.Value : print)}</>";
        }
        else
            value = print;

        return value;
    }

    // public ChannelReader<EntityChangeDisplay> GetChangesets(
    //     CancellationToken cancellationToken)
    // {
    //     var channel = Channel.CreateUnbounded<EntityChangeDisplay>();
    //
    //     // We don't want to await WriteItemsAsync, otherwise we'd end up waiting 
    //     // for all the items to be written before returning the channel back to
    //     // the client.
    //     _ = WriteChangesetsAsync(channel.Writer, cancellationToken);
    //
    //     return channel.Reader;
    // }
    //
    // private async Task WriteChangesetsAsync(
    //     ChannelWriter<EntityChangeDisplay> writer,
    //     CancellationToken cancellationToken)
    // {
    //     Exception? localException = default;
    //     try
    //     {
    //         int lastChangeset = 0;
    //         while (true)
    //         {
    //             while (_db!.History!.Changesets.Count > 0 && lastChangeset < _db.History.Changesets.Count)
    //             {
    //                 var changeset = _db.History.Changesets[lastChangeset++];
    //                 if((changeset.Changes.Count) > 0)
    //                     foreach (var entityChangeDisplay in GetChangesetDetails(changeset))
    //                         await writer.WriteAsync(entityChangeDisplay, cancellationToken);
    //             }
    //
    //             await Task.Delay(500, cancellationToken);
    //         }
    //     }
    //     catch (Exception e)
    //     {
    //         localException = e;
    //     }
    //     finally
    //     {
    //         writer.Complete(localException);
    //     }
    // }
    public int GetChangesetsCount() => _db?.History?.Changesets.Count ?? 0;
    public IEnumerable<EntityChangeDisplay> GetChangesets(int start, int count)
    {
        if(_db.History == null)            return (ArraySegment<EntityChangeDisplay>.Empty);
    
        var r = _db.History.Changesets.Count - start;
        if (r <= 0)
            return (ArraySegment<EntityChangeDisplay>.Empty);
        var c = Math.Min(count, r);
        return //(r - count,
            _db.History.Changesets.Skip(start).Take(c).SelectMany(GetChangesetDetails) //);
            ;
    }

    public IEnumerable<EntityChangeDisplay> GetEntityChangesets(uint eid)
    {
        if (_db?.History == null) return ArraySegment<EntityChangeDisplay>.Empty;
        Mutex.Wait();
        try
        {
            return _db.History.Changesets
                .SelectMany(cs => cs.Changes
                    .Where(x => x.New.Id.Id == eid)
                    .Select(x => new EntityChangeDisplay(x.New.Id, cs.Year, cs.ActionName, GetChangeDetails(x))))
                .ToList();
        }
        finally
        {
            Mutex.Release();
        }
    }

    public ChannelReader<Message> Stream(
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<Message>();

        // We don't want to await WriteItemsAsync, otherwise we'd end up waiting 
        // for all the items to be written before returning the channel back to
        // the client.
        _ = WriteItemsAsync(channel.Writer, cancellationToken);

        return channel.Reader;
    }

    public struct Message(Database.Record? record)
    {
        public enum MessageType
        {
            Reset,
            Record,
            Year
        }

        public MessageType Type = MessageType.Record;
        public Database.Record? Record = record;
        public long Year;

        public static Message Reset(long? targetYears) => new Message() { Type = MessageType.Reset, Year = targetYears.GetValueOrDefault(0) };
        public static Message YearMessage(long year) => new Message() { Type = MessageType.Year, Year = year, };
    }

    private async Task WriteItemsAsync(
        ChannelWriter<Message> writer,
        CancellationToken cancellationToken)
    {
        Exception? localException = null;
        try
        {
            Debug.WriteLine("Stream");
            int lastRecord = 0;
            while (true)
            {
                if (_resetRequested)
                {
                    _resetRequested = false;
                    Reset();
                }
                if (_reset)
                {
                    _reset = false;
                    await writer.WriteAsync(Message.Reset(_targetYears), cancellationToken);
                    _targetYears = null;
                    lastRecord = 0;
                    // if (_targetYears.HasValue)
                    // {
                    //     var y = _targetYears.Value;
                    //     _targetYears = null;
                    // }
                }

                while (_db!.Records.Count > 0 && lastRecord < _db.Records.Count)
                {
                    await writer.WriteAsync(new Message(_db.Records[lastRecord++]), cancellationToken);
                }

                await writer.WriteAsync(Message.YearMessage(_db.Ctx.Year), cancellationToken);
                await Task.Delay(500, cancellationToken);
                
            }
        }
        catch (Exception? e)
        {
            localException = e;
        }
        finally
        {
            writer.Complete(localException);
        }
    }
}
