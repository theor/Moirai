using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Channels;
using Moirai.Core;

namespace MoiraiWebServer.Hubs;

using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    private static Database _db;
    private static bool _reset;

    private static object _mutex = new();
    public ChatHub()
    {
        lock (_mutex)
        {
            if (_db == null)
            {
                Reset();
            }

            Debug.WriteLine("Ctor");
        }
    }

    public void Reset()
    {
        lock (_mutex)
        {
            _db = StoryParser.Parse(File.ReadAllText(@"C:\Users\theor\Moirai\MoiraiCli\w.sg"), out var errors);
            _db.History = new();
            _db.Init();
            _reset = true;
        }
    }

    public async Task PassYears(int years)
    {
        lock (_mutex)
            _db.Ctx.PassYears(years, true);
    }

    public void Save()
    {
        lock (_mutex)
            _db.Commit();
    }

    public struct ClientData
    {
        public record ActionData(int Id, string Name);

        public ActionData[] Actions;
    }

    public async Task<ClientData> GetClientData()
    {
        lock (_mutex)
            return new ClientData
            {
                Actions = _db.Actions.Select(a => new ClientData.ActionData(a.Id, a.Name)).ToArray(),
            };
    }

    public record EntityPropertyDisplay(string Label, string Value);
    private static List<EntityId> results = new();

    public record EntityChangeDisplay(EntityId id, long year, string actionName, IList<EntityPropertyDisplay> changes);

    private IList<EntityPropertyDisplay> GetChangeDetails(Changeset.Changed c)
    {
        lock (_mutex)
        {
            if (c.Prev.Id.IsNull) // new entity
            {
                return c.New.Properties.Where(p => p.Id.IsValid)
                    .Select(p => new EntityPropertyDisplay(_db.GetPropertyName(p.Id), PrintValue(p.Id, p.Value)))
                    .ToList();
            }

            return c.Prev.Properties.Where(p => p.Id.IsValid)
                .Select(p =>
                {
                    var p1 = c.New.GetProperty(p.Id);
                    return new EntityPropertyDisplay(_db.GetPropertyName(p.Id),
                        PrintValue(p.Id, p.Value) + " -> " + PrintValue(p.Id, p1));
                }).ToList();
        }
    }

    public struct QueryResult
    {
        public string? Sql;
        public Result[] Results;
        public string[] Errors;
    }
    public struct Result
    {
        public EntityId Eid;
        public IList<EntityPropertyDisplay> Properties;
    }
    public async Task<QueryResult> Query(string q)
    {
        lock (_mutex)
        {
            string? sql = null;
            try
            {
                StoryParser.AstVisitor v = new StoryParser.AstVisitor(_db);
                var e = StoryParser.ParseExpr(v, q, 0, 0, out var errors);
                if (errors.Any())
                    return new QueryResult { Errors = errors.Select(e => e.ToString()).ToArray() };
                if (e is AssignPick pick)
                {
                    _db.FindAll(pick.EntityType, pick.Value, ref results, out sql);
                    return new QueryResult
                    {
                        Sql = sql,
                        Results = results.Select(eid => new Result
                        {
                            Eid = eid, Properties = GetEntityDetails(eid.Id),
                        }).ToArray()
                    };
                }
                return  new QueryResult() { Errors = new[]{ "Instruction unsuited for query: " + e.GetType() } };
            }
            catch (Exception e)
            {
                return  new QueryResult() { Sql = sql, Errors = new[]{ e.ToString() } };
            }
        }
    }

    public void RunAction(int actionId)
    {
        lock (_mutex)
        {
            var eventTrigger = _db.Actions.FirstOrDefault(a => a.Id == actionId);
            if(eventTrigger != null)
                _db.RunAction(eventTrigger);
        }
    }

    private IList<EntityChangeDisplay> GetChangesetDetails(Changeset cs)
    {
        lock (_mutex)
        return cs.Changes.Select(x => new EntityChangeDisplay(x.New.Id, cs.Year, cs.ActionName, GetChangeDetails(x))).ToList();
    }
    public IList<EntityPropertyDisplay> GetEntityDetails(uint eid)
    {
        lock (_mutex)
        {
            if (!_db.TryGetEntity(new EntityId(eid), out var e))
                return ImmutableList<EntityPropertyDisplay>.Empty;
            var details = e.Properties.Where(p => p.Id.IsValid)
                .Select(p => new EntityPropertyDisplay(
                    _db.GetPropertyName(p.Id),
                    PrintValue(p.Id, p.Value))).ToList();
            var t = _db.GetEntityType(e.Type);
            foreach (var display in t.Attributes)
            {
                using var _ = _db.Ctx.RunScope(false);
                _db.Ctx.SetArgument(display.VarIndex, e.Id);
                _db.FindAll(display.ReferencedType.Id, display.Value, ref results);
                foreach (var id in results)
                {
                    if (_db.TryGetEntity(id, out var ee))
                        details.Add(new EntityPropertyDisplay(display.Label,
                            $"<{ee.Id}>{(_db.GetProperty(ee.Id, Database.PropName, out var val) ? val.Value : ee.Id)}</>"));
                }
            }

            return details;
        }
    }

    private static string PrintValue(PropertyId propertyId, PropertyValue propertyValue)
    {
        var print = _db.Printer.Print(propertyValue);
        string value;
        if (_db.GetPropertyType(propertyId, out var type) && type.IsRefType)
        {
            if (propertyValue.Id.IsNull)
                value = "null";
            else
                value =
                    $"<{print}>{(_db.GetProperty(propertyValue.Id, Database.PropName, out var val) ? val.Value : print)}</>";
        }
        else
            value = print;

        return value;
    }

    public ChannelReader<EntityChangeDisplay> GetChangesets(
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<EntityChangeDisplay>();

        // We don't want to await WriteItemsAsync, otherwise we'd end up waiting 
        // for all the items to be written before returning the channel back to
        // the client.
        _ = WriteChangesetsAsync(channel.Writer, cancellationToken);

        return channel.Reader;
    }
    
    private async Task WriteChangesetsAsync(
        ChannelWriter<EntityChangeDisplay> writer,
        CancellationToken cancellationToken)
    {
        Exception localException = null;
        try
        {
            int lastChangeset = 0;
            while (true)
            {
                while (_db.History.Changesets.Count > 0 && lastChangeset < _db.History.Changesets.Count)
                {
                    var changeset = _db.History.Changesets[lastChangeset++];
                    if((changeset.Changes?.Count ?? 0) > 0)
                        foreach (var entityChangeDisplay in GetChangesetDetails(changeset))
                            await writer.WriteAsync(entityChangeDisplay, cancellationToken);
                }

                await Task.Delay(500);
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
    // public (int remaining, IEnumerable<Changeset> changesets) GetChangesets(int start, int count)
    // {
    //     if(_db.History == null)            return (0, ArraySegment<Changeset>.Empty);
    //
    //     var r = _db.History.Changesets.Count - start;
    //     if (r <= 0)
    //         return (0, ArraySegment<Changeset>.Empty);
    //     var c = Math.Min(count, r);
    //     return (r - count, _db.History.Changesets.Skip(start).Take(c));
    // }

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

    public struct Message
    {
        public enum MessageType
        {
            Reset,
            Record,
            Year
        }

        public MessageType Type;
        public Database.Record? Record;
        public long Year;

        public Message(Database.Record? record)
        {
            Type = MessageType.Record;
            Record = record;
        }

        public static Message Reset() => new Message() { Type = MessageType.Reset };
        public static Message YearMessage(long year) => new Message() { Type = MessageType.Year, Year = year, };
    }

    private async Task WriteItemsAsync(
        ChannelWriter<Message> writer,
        CancellationToken cancellationToken)
    {
        Exception localException = null;
        try
        {
            Debug.WriteLine("Stream");
            int lastRecord = 0;
            while (true)
            {
                if (_reset)
                {
                    _reset = false;
                    await writer.WriteAsync(Message.Reset(), cancellationToken);
                    lastRecord = 0;
                }

                while (_db.Records.Count > 0 && lastRecord < _db.Records.Count)
                {
                    await writer.WriteAsync(new Message(_db.Records[lastRecord++]), cancellationToken);
                }

                await writer.WriteAsync(Message.YearMessage(_db.Ctx.Year));
                await Task.Delay(500);
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
