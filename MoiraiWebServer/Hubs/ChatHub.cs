using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MoiraiWebServer.Hubs;

using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    private static Database _db;

    public ChatHub()
    {
        if (_db == null)
        {
            _db = StoryParser.Parse(File.ReadAllText(@"C:\Users\theor\Moirai\MoiraiCli\w.sg"), out var errors);
            _db.Init();
        }
    }

    public async Task PassYears(int years)
    {
        _db.Ctx.PassYears(years, true);
    }

    public struct ClientData
    {
        public (int Id, string Name)[] Actions { get; set; }
    }
    public async Task<ClientData> GetClientData()
    {
        return new ClientData
        {
            Actions = _db.Actions.Select(a => (a.Id, a.Name)).ToArray(),
        };
    }
    public async Task NewMessage(string username, string message)
    {
        Debug.WriteLine($"Received {username} {message}");
        await Clients.All.SendAsync("messageReceived", username, message);
    }
    
    public async IAsyncEnumerable<Database.Record> Counter(
        int count,
        int delay,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        int lastRecord = 0;
        while (true)
        {
            while (_db.Records.Count > 0 && lastRecord < _db.Records.Count)
            {
                yield return _db.Records[lastRecord++];
            }
            await Task.Delay(1000);
        }
    }
}
