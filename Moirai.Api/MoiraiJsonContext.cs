using System.Text.Json.Serialization;
using Moirai.Core;

namespace Moirai.Api;

/// <summary>
/// Source-generated serializers for every type that crosses the wire.
///
/// <para>Reflection-based <see cref="System.Text.Json.JsonSerializer"/> works, but it forces the
/// WebAssembly build to keep whole assemblies: trimming strips constructor parameter names, and without
/// them the reflective resolver cannot bind a record's constructor. Generating the readers and writers at
/// compile time removes that dependency, so the linker is free to trim our code as well as the
/// framework's — and a missing type becomes a build error here rather than a runtime one in a browser.</para>
///
/// <para>Every type reachable from a <see cref="WorldSession"/> return value has to be listed. The
/// scalars are here because the WebAssembly host returns them straight from <c>Invoke</c>.</para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    IncludeFields = true,
    IgnoreReadOnlyProperties = true,
    UseStringEnumConverter = true,
    Converters = [
        typeof(EntityIdConverter),
        typeof(PropertyIdConverter),
        typeof(EntityTypeIdConverter),
        typeof(ValueTypeConverter),
    ])]
[JsonSerializable(typeof(ClientData))]
[JsonSerializable(typeof(Biography))]
[JsonSerializable(typeof(WorldOverview))]
[JsonSerializable(typeof(TimeSeries))]
[JsonSerializable(typeof(RuleCoverageReport))]
[JsonSerializable(typeof(QueryResult))]
[JsonSerializable(typeof(StoryApplyResult))]
[JsonSerializable(typeof(StoryDiagnostic[]))]
[JsonSerializable(typeof(FeedTick))]
[JsonSerializable(typeof(List<Message>))]
[JsonSerializable(typeof(List<EntityChangeDisplay>))]
[JsonSerializable(typeof(List<FamilyTreeNode>))]
[JsonSerializable(typeof(IList<EntityPropertyDisplay>))]
[JsonSerializable(typeof(List<EntityPropertyDisplay>))]
[JsonSerializable(typeof(Database.Record))]
[JsonSerializable(typeof(PropertyValue.ValueBaseType))]
// Returned bare by the WebAssembly host's Invoke.
[JsonSerializable(typeof(long))]
// GetStory returns the whole .sg as one string.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(int))]
public partial class MoiraiJsonContext : JsonSerializerContext;
