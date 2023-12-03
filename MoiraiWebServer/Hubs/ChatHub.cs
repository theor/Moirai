using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace MoiraiWebServer.Hubs;

using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    private static Database _db;
    private static bool _reset;

    public ChatHub()
    {
        if (_db == null)
        {
            Reset();
        }

        Debug.WriteLine("Ctor");
    }

    public void Reset()
    {
        _db = StoryParser.Parse(File.ReadAllText(@"C:\Users\theor\Moirai\MoiraiCli\w.sg"), out var errors);
        _db.Init();
        _reset = true;
    }

    public async Task PassYears(int years)
    {
        _db.Ctx.PassYears(years, true);
    }

    public void Save()
    {
        _db.Commit();
    }

    public struct ClientData
    {
        public record ActionData(int Id, string Name);

        public ActionData[] Actions { get; set; }
    }

    public async Task<ClientData> GetClientData()
    {
        return new ClientData
        {
            Actions = _db.Actions.Select(a => new ClientData.ActionData(a.Id, a.Name)).ToArray(),
        };
    }

    public record EntityPropertyDisplay(string Label, string Value);
    private static List<EntityId> results = new();

    public IList<EntityPropertyDisplay> GetEntityDetails(uint eid)
    {
        if (!_db.TryGetEntity(new EntityId(eid), out var e))
            return ImmutableList<EntityPropertyDisplay>.Empty;
        var details = e.Properties.Where(p => p.Id.IsValid)
            .Select(p =>
            {
                var print = _db.Printer.Print(p.Value);
                string value;
                if (_db.GetPropertyType(p.Id, out var type) && type.IsRefType)
                {
                    if (p.Value.Id.IsNull)
                        value = "null";
                    else
                        value =
                            $"<{print}>{(_db.GetProperty(p.Value.Id, Database.PropName, out var val) ? val.Value : print)}</>";
                }
                else
                    value = print;

                return new EntityPropertyDisplay(_db.GetPropertyName(p.Id),
                    value);
            }).ToList();
        var t = _db.GetEntityType(e.Type);
        foreach (var display in t.Attributes)
        {
            _db.Ctx.SetArgument(display.VarIndex, e.Id);
            _db.FindAll(display.Value, ref results);
            foreach (var id in results)
            {
                if(_db.TryGetEntity(id, out var ee))
                    details.Add(new EntityPropertyDisplay(display.Label, $"<{ee.Id}>{(_db.GetProperty(ee.Id, Database.PropName, out var val) ? val.Value : ee.Id)}</>"));
            }
        }
        return details;
    }

    public async Task NewMessage(string username, string message)
    {
        Debug.WriteLine($"Received {username} {message}");
        await Clients.All.SendAsync("messageReceived", username, message);
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
