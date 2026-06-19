using System.Net;
using System.Net.Sockets;

namespace Moirai.DebugAdapter;

/// <summary>
/// Listens on a loopback TCP port for DAP clients (VS Code connects via
/// <c>DebugAdapterServer(port)</c>) and runs a <see cref="DapServer"/> per connection.
/// </summary>
public sealed class DapListener
{
    private readonly IDebugHost _host;
    private readonly int _port;
    private TcpListener? _listener;

    public DapListener(IDebugHost host, int port)
    {
        _host = host;
        _port = port;
    }

    /// <summary>The actual bound port (useful when constructed with port 0 to auto-pick).</summary>
    public int Port => _listener?.LocalEndpoint is IPEndPoint ep ? ep.Port : _port;

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        new Thread(AcceptLoop) { IsBackground = true, Name = "moirai-dap-listener" }.Start();
    }

    private void AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try { client = _listener!.AcceptTcpClient(); }
            catch { break; }

            new Thread(() =>
            {
                try
                {
                    using (client)
                    using (var stream = client.GetStream())
                    {
                        var conn = new DapConnection(stream, stream);
                        new DapServer(conn, _host).Run();
                    }
                }
                catch
                {
                    // A dropped client must not take down the listener.
                }
            }) { IsBackground = true, Name = "moirai-dap-session" }.Start();
        }
    }
}
