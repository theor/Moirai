using System.Text;
using System.Text.Json.Nodes;
using Moirai.DebugAdapter;

namespace TestProject1;

/// <summary>Wire-format round-trip tests for the DAP codec (<see cref="DapConnection"/>).</summary>
public class DapProtocolTests
{
    [Test]
    public void ReadsFramedRequest()
    {
        var json = "{\"seq\":1,\"type\":\"request\",\"command\":\"initialize\"}";
        var framed = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";
        var input = new MemoryStream(Encoding.UTF8.GetBytes(framed));
        var conn = new DapConnection(input, new MemoryStream());

        var msg = conn.Read();

        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!["type"]!.GetValue<string>(), Is.EqualTo("request"));
        Assert.That(msg["command"]!.GetValue<string>(), Is.EqualTo("initialize"));
        Assert.That(conn.Read(), Is.Null, "second read should hit EOF");
    }

    [Test]
    public void WritesFramedEvent()
    {
        var output = new MemoryStream();
        var conn = new DapConnection(new MemoryStream(), output);

        conn.SendEvent("stopped", new JsonObject { ["reason"] = "breakpoint", ["threadId"] = 1 });

        var written = Encoding.UTF8.GetString(output.ToArray());
        Assert.That(written, Does.StartWith("Content-Length: "));

        int split = written.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.That(split, Is.GreaterThan(0));
        var body = written[(split + 4)..];

        // The advertised length must match the actual body byte count.
        var lengthLine = written[..written.IndexOf("\r\n", StringComparison.Ordinal)];
        int advertised = int.Parse(lengthLine["Content-Length: ".Length..]);
        Assert.That(Encoding.UTF8.GetByteCount(body), Is.EqualTo(advertised));

        var parsed = JsonNode.Parse(body) as JsonObject;
        Assert.That(parsed!["type"]!.GetValue<string>(), Is.EqualTo("event"));
        Assert.That(parsed["event"]!.GetValue<string>(), Is.EqualTo("stopped"));
        Assert.That((parsed["body"] as JsonObject)!["reason"]!.GetValue<string>(), Is.EqualTo("breakpoint"));
    }

    [Test]
    public void ResponseEchoesRequestSeqAndCommand()
    {
        var output = new MemoryStream();
        var conn = new DapConnection(new MemoryStream(), output);
        var request = new JsonObject { ["seq"] = 42, ["type"] = "request", ["command"] = "threads" };

        conn.SendResponse(request, true, new JsonObject { ["threads"] = new JsonArray() });

        var written = Encoding.UTF8.GetString(output.ToArray());
        var body = written[(written.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..];
        var parsed = (JsonObject)JsonNode.Parse(body)!;
        Assert.That(parsed["type"]!.GetValue<string>(), Is.EqualTo("response"));
        Assert.That(parsed["request_seq"]!.GetValue<int>(), Is.EqualTo(42));
        Assert.That(parsed["command"]!.GetValue<string>(), Is.EqualTo("threads"));
        Assert.That(parsed["success"]!.GetValue<bool>(), Is.True);
    }
}
