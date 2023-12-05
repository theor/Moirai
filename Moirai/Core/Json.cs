using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moirai.Core;

public class EntityIdConverter : JsonConverter<EntityId>
{
    public override EntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntityId((uint)reader.GetInt64());
    }
    public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Id);
    }
}
public class PropertyIdConverter : JsonConverter<PropertyId>
{
    public override PropertyId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new PropertyId(reader.GetUInt32());
    }
    public override void Write(Utf8JsonWriter writer, PropertyId value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Id);
    }
}

public class ValueTypeConverter : JsonConverter<PropertyValue.ValueType>
{
    // private JsonStringEnumConverter _e = new();
    public override PropertyValue.ValueType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if(reader.TokenType != JsonTokenType.StartArray)
            throw new System.NotImplementedException("no start array");

        reader.Read();
        PropertyValue.ValueBaseType baseType = JsonSerializer.Deserialize<PropertyValue.ValueBaseType>(ref reader, options);
        reader.Read();
        var index = reader.GetUInt16();
        reader.Read();
        if(reader.TokenType != JsonTokenType.EndArray)
            throw new System.NotImplementedException("no end array");

        return new PropertyValue.ValueType(baseType, index);
    }
    public override void Write(Utf8JsonWriter writer, PropertyValue.ValueType value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        JsonSerializer.Serialize(writer, value.BaseType);
        JsonSerializer.Serialize(writer, value.Index);

        writer.WriteEndArray();
    }
}

public class EntityTypeIdConverter : JsonConverter<EntityTypeId>
{
    public override EntityTypeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntityTypeId(reader.GetUInt32());
    }
    public override void Write(Utf8JsonWriter writer, EntityTypeId value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Id);
    }
}
