using System.Text.Json;
using System.Text.Json.Serialization;

namespace KaraokeToolkit.Core.Execution;

/// <summary>Operation identities must never depend on an enum's numeric ordering.</summary>
internal sealed class OperationIdJsonConverter : JsonConverter<OperationId>
{
    public override OperationId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } name &&
            Enum.TryParse<OperationId>(name, out var value) && Enum.IsDefined(value) && value.ToString() == name)
            return value;
        throw new JsonException("An operation must be identified by its defined name.");
    }

    public override void Write(Utf8JsonWriter writer, OperationId value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value)) throw new JsonException("Unknown operation identity.");
        writer.WriteStringValue(value.ToString());
    }
}
