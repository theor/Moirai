using System.Text;
using System.Text.Json.Nodes;

namespace Moirai.DebugAdapter;

/// <summary>
/// Minimal Debug Adapter Protocol wire codec: messages are
/// <c>Content-Length: N\r\n\r\n{json}</c> over a byte stream (same framing as LSP).
/// Reads/writes JSON object messages; thread-safe on the write side.
/// </summary>
public sealed class DapConnection
{
    private readonly Stream _in;
    private readonly Stream _out;
    private readonly object _writeLock = new();
    private int _seq;

    public DapConnection(Stream input, Stream output)
    {
        _in = input;
        _out = output;
    }

    /// <summary>Read one message; returns null at end of stream.</summary>
    public JsonObject? Read()
    {
        int contentLength = -1;
        // Headers, terminated by a blank line.
        while (true)
        {
            var line = ReadHeaderLine();
            if (line == null)
                return null;               // EOF
            if (line.Length == 0)
                break;                     // end of headers
            int colon = line.IndexOf(':');
            if (colon > 0 &&
                line.AsSpan(0, colon).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line.AsSpan(colon + 1).Trim(), out contentLength);
            }
        }

        if (contentLength < 0)
            return null;

        var body = new byte[contentLength];
        int read = 0;
        while (read < contentLength)
        {
            int n = _in.Read(body, read, contentLength - read);
            if (n <= 0)
                return null;
            read += n;
        }

        var text = Encoding.UTF8.GetString(body);
        return JsonNode.Parse(text) as JsonObject;
    }

    // Read a single CRLF-terminated header line as ASCII (returns null at EOF, "" for the blank line).
    private string? ReadHeaderLine()
    {
        var sb = new StringBuilder();
        int b;
        while ((b = _in.ReadByte()) != -1)
        {
            if (b == '\r')
            {
                int next = _in.ReadByte();
                if (next == '\n')
                    return sb.ToString();
                if (next == -1)
                    return sb.Length == 0 ? null : sb.ToString();
                sb.Append((char)b);
                sb.Append((char)next);
                continue;
            }

            sb.Append((char)b);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private void WriteRaw(JsonObject message)
    {
        var json = message.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
        lock (_writeLock)
        {
            try
            {
                _out.Write(header, 0, header.Length);
                _out.Write(bytes, 0, bytes.Length);
                _out.Flush();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                // The client went away mid-write; a dropped connection must never crash the host.
            }
        }
    }

    public void SendResponse(JsonObject request, bool success, JsonObject? body = null, string? message = null)
    {
        var response = new JsonObject
        {
            ["seq"] = NextSeq(),
            ["type"] = "response",
            ["request_seq"] = request["seq"]?.GetValue<int>() ?? 0,
            ["success"] = success,
            ["command"] = request["command"]?.GetValue<string>() ?? "",
        };
        if (message != null) response["message"] = message;
        if (body != null) response["body"] = body;
        WriteRaw(response);
    }

    /// <summary>Send a request (client side; used by tests/tools driving an adapter).</summary>
    public void SendRequest(string command, JsonObject? arguments = null)
    {
        var req = new JsonObject
        {
            ["seq"] = NextSeq(),
            ["type"] = "request",
            ["command"] = command,
        };
        if (arguments != null) req["arguments"] = arguments;
        WriteRaw(req);
    }

    public void SendEvent(string eventName, JsonObject? body = null)
    {
        var ev = new JsonObject
        {
            ["seq"] = NextSeq(),
            ["type"] = "event",
            ["event"] = eventName,
        };
        if (body != null) ev["body"] = body;
        WriteRaw(ev);
    }

    private int NextSeq() => Interlocked.Increment(ref _seq);
}
