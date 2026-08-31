using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using Moirai.Api;

namespace Moirai.Wasm;

/// <summary>
/// The JavaScript boundary for a browser-hosted Moirai world.
///
/// <para><b>Why everything crosses as a JSON string.</b> <c>[JSExport]</c> marshals primitives, not object
/// graphs, so the DTOs have to be serialized somewhere regardless. Doing it with
/// <see cref="MoiraiWireJson.Options"/> means the browser produces exactly the bytes the SignalR server
/// produces, and the client's hand-written TypeScript types work against either without knowing which it
/// is talking to.</para>
///
/// <para><b>Why one <see cref="Invoke"/> instead of one export per method.</b> A single
/// name-plus-arguments entry point is the same shape as SignalR's own <c>invoke</c>, which is what keeps
/// the two client-side implementations near-identical — and it means adding a method to
/// <see cref="WorldSession"/> needs one <c>case</c> here rather than a new export, a new signature and a
/// new JS binding.</para>
///
/// <para><b>Why there is no progress callback.</b> The runtime only initialises on the browser's main
/// thread — in a dedicated worker it loads every assembly and then never finishes starting — so the host
/// simulates in short chunks and yields between them. Progress is therefore whatever fraction of the
/// chunks JavaScript has asked for, and a callback out of a synchronous pass would have nothing to add:
/// it could not yield, so the page would not repaint until the pass was over anyway.</para>
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class MoiraiInterop
{
    private static WorldSession? _session;

    private static WorldSession Session =>
        _session ?? throw new InvalidOperationException("Load must be called before any other call.");

    /// <summary>
    /// Build the world. <paramref name="seed"/> is a string because <c>ulong</c> has no JS marshalling and
    /// a seed above 2^53 would not survive a <c>double</c>.
    /// </summary>
    [JSExport]
    internal static void Load(string storyText, string seed)
    {
        // Console output would otherwise be one console.log per year per event, which in a browser costs
        // more than the simulation it is reporting on.
        Database.Log = _ => { };
        _session = new WorldSession(storyText, ulong.TryParse(seed, out var s) ? s : 42UL);
    }

    /// <summary>
    /// Call a session method by its wire name. <paramref name="argsJson"/> is a JSON array of positional
    /// arguments; the result is the method's return value as JSON, or <c>"null"</c> for a void method.
    /// </summary>
    [JSExport]
    internal static string Invoke(string method, string argsJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "[]" : argsJson);
        var args = doc.RootElement;

        long Int(int i) => args[i].GetInt64();
        string Str(int i) => args[i].GetString() ?? "";

        // The names are the SignalR hub's method names, so both transports speak the same vocabulary.
        return method switch
        {
            "Reset" => Json(Session.Reset()),
            "Reseed" => Json(Session.Reseed((ulong)Int(0))),
            "GetSeed" => Json(Session.GetSeed()),
            "Save" => Void(Session.Save),
            "RunAction" => Void(() => Session.RunAction((int)Int(0))),
            "GetClientData" => Json(Session.GetClientData()),
            "GetBiography" => Json(Session.GetBiography((uint)Int(0))),
            "GetWorldOverview" => Json(Session.GetWorldOverview()),
            "GetPropertySeries" => Json(Session.GetPropertySeries((int)Int(0), Str(1))),
            "GetRuleCoverage" => Json(Session.GetRuleCoverage()),
            "Query" => Json(Session.Query(Str(0))),
            "GetFamilyTree" => Json(Session.GetFamilyTree((uint)Int(0), (int)Int(1))),
            "GetEntityDetails" => Json(Session.GetEntityDetails((uint)Int(0))),
            "GetChangesetsCount" => Json(Session.GetChangesetsCount()),
            "GetChangesets" => Json(Session.GetChangesets((int)Int(0), (int)Int(1))),
            "GetEntityChangesets" => Json(Session.GetEntityChangesets((uint)Int(0))),
            _ => throw new ArgumentException($"Unknown method '{method}'."),
        };
    }

    /// <summary>
    /// Simulate <paramref name="years"/> years and return the year reached. One chunk of a longer pass:
    /// the host calls this repeatedly, yielding to the event loop between calls so the page stays
    /// responsive. Chunking cannot change the result — the RNG streams live on the execution context and
    /// the year is re-read from the <c>Time</c> singleton on entry, which
    /// <c>WorldSessionTests.ManySmallPassesAreIdenticalToOneLongPass</c> pins.
    /// </summary>
    [JSExport]
    internal static string PassYears(int years)
    {
        Session.PassYears(years);
        return Json(Session.Year);
    }

    /// <summary>
    /// One tick of the record feed: the messages after <paramref name="cursor"/> records, and the cursor
    /// to pass back next time. Shares <see cref="WorldSession.DrainFeed"/> with the server, so both
    /// viewers see the same sequence.
    /// </summary>
    [JSExport]
    internal static string StreamTick(int cursor)
    {
        var batch = Session.DrainFeed(cursor, out var newCursor);
        return Json(new FeedTick(newCursor, batch));
    }

    private record FeedTick(int Cursor, List<Message> Messages);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, MoiraiWireJson.Options);

    private static string Void(Action action)
    {
        action();
        return "null";
    }
}
