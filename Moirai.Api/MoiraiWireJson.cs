using System.Text.Json;
using System.Text.Json.Serialization;
using Moirai.Core;

namespace Moirai.Api;

/// <summary>
/// The one definition of Moirai's viewer wire format.
///
/// The Svelte client's TypeScript types are written by hand against this shape, so both hosts must
/// produce it byte for byte: the SignalR server applies <see cref="Configure"/> to its payload
/// serializer, and the WebAssembly host serializes with <see cref="Options"/>. Get them out of step and
/// the failure is silent — properties simply arrive under names the client never reads.
/// </summary>
public static class MoiraiWireJson
{
    /// <summary>
    /// Apply the settings every Moirai host shares. Kept separate from <see cref="Options"/> because
    /// SignalR hands us a pre-built <see cref="JsonSerializerOptions"/> to mutate rather than letting us
    /// supply our own.
    /// </summary>
    public static void Configure(JsonSerializerOptions o)
    {
        // Load-bearing: ClientData, QueryResult, Result, Message and Database.Record expose public
        // fields, not properties. Without this they serialize as empty objects.
        o.IncludeFields = true;
        o.IgnoreReadOnlyProperties = true;

        // The client's MessageType is a string union, so enums must not go over as numbers.
        o.Converters.Add(new JsonStringEnumConverter());

        // The engine's id structs collapse to plain numbers (and ValueType to a pair) so the client
        // never has to know they are structs. See Moirai/Core/Json.cs.
        o.Converters.Add(new EntityIdConverter());
        o.Converters.Add(new PropertyIdConverter());
        o.Converters.Add(new EntityTypeIdConverter());
        o.Converters.Add(new ValueTypeConverter());

        // Inserted, not assigned: the generated resolver answers for the wire types, and on a host where
        // reflection is available (the server, whose hub also serializes its own framing types) the
        // default resolver stays behind it as a fallback. In the browser, where reflection is switched
        // off, the chain is this and nothing else — which is the point.
        o.TypeInfoResolverChain.Insert(0, MoiraiJsonContext.Default);
    }

    /// <summary>
    /// Options for a host that serializes on its own, i.e. the WebAssembly build. Identical to what the
    /// server produces, including the camelCase policy — which SignalR supplies by default and we
    /// therefore have to opt into explicitly here.
    /// </summary>
    public static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Configure(o);
        return o;
    }
}
