using KaraokeToolkit.Core.Archives;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace KaraokeToolkit.Core.Conversion;

/// <summary>Reads pre-migration recovery options while writing only the named ZIP preference.</summary>
internal sealed class ConversionOptionsJsonConverter : JsonConverter<ConversionOptions>
{
    public override ConversionOptions Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var value = new ConversionOptions();
        foreach (var property in typeof(ConversionOptions).GetProperties())
        {
            if (!root.TryGetProperty(property.Name, out var field)) continue;
            object? parsed = property.Name == nameof(ConversionOptions.ZipCompression)
                ? ZipCompressionMigration.FromSaved(field.GetString()!)
                : field.Deserialize(property.PropertyType, options);
            property.SetValue(value, parsed);
        }
        if (!root.TryGetProperty(nameof(ConversionOptions.ZipCompression), out _) &&
            root.TryGetProperty("ZipCompressionLevel", out var legacy))
            value = value with { ZipCompression = ZipCompressionMigration.FromLegacy(legacy.GetInt32()) };
        if (!Enum.IsDefined(value.ZipCompression)) throw new JsonException("Invalid ZIP compression preference.");
        if (!Enum.IsDefined(value.ZipAlgorithm)) throw new JsonException("Invalid ZIP algorithm.");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, ConversionOptions value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var property in typeof(ConversionOptions).GetProperties())
        {
            writer.WritePropertyName(property.Name);
            if (property.Name == nameof(ConversionOptions.ZipCompression)) writer.WriteStringValue(ZipCompressionMigration.ToLegacy(value.ZipCompression).ToString(System.Globalization.CultureInfo.InvariantCulture));
            else JsonSerializer.Serialize(writer, property.GetValue(value), property.PropertyType, options);
        }
        writer.WriteEndObject();
    }
}
