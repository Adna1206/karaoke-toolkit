using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.Mcp;

/// <summary>Strict MCP settings use current levels and safe defaults, independent of persisted-data migrations.</summary>
public sealed class McpConversionOptionsConverter : JsonConverter<ConversionOptions>
{
    private static readonly PropertyInfo[] Properties = typeof(ConversionOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public);
    public override ConversionOptions Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return ReadOptions(document.RootElement, new ConversionOptions { DeleteOldFiles = false }, options);
    }

    internal static ConversionOptions ReadOptions(JsonElement document, ConversionOptions defaults, JsonSerializerOptions options)
    {
        if (document.ValueKind != JsonValueKind.Object) throw new JsonException("Options must be an object.");
        var result = defaults with { };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in document.EnumerateObject())
        {
            var property = Properties.SingleOrDefault(p => field.Name == p.Name || field.Name == JsonNamingPolicy.CamelCase.ConvertName(p.Name))
                ?? throw new JsonException("Unknown conversion option: " + field.Name);
            if (!seen.Add(property.Name)) throw new JsonException("Repeated conversion option: " + property.Name);
            object? value;
            if (property.Name == nameof(ConversionOptions.ZipCompression))
            {
                string text = field.Value.ValueKind == JsonValueKind.Number ? field.Value.GetInt32().ToString() : field.Value.GetString() ?? "";
                if (!int.TryParse(text, out int number))
                {
                    if (!Enum.TryParse<ZipCompression>(text, true, out var named) || !Enum.IsDefined(named)) throw new JsonException("ZIP level must be 0-9 or a current level name.");
                    number = (int)named;
                }
                value = ZipCompressionMigration.FromLegacy(number);
            }
            else value = field.Value.Deserialize(property.PropertyType, options);
            property.SetValue(result, value);
        }
        return result;
    }
    public override void Write(Utf8JsonWriter writer, ConversionOptions value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var property in Properties)
        {
            writer.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
            if (property.Name == nameof(ConversionOptions.ZipCompression)) writer.WriteStringValue(((int)value.ZipCompression).ToString());
            else JsonSerializer.Serialize(writer, property.GetValue(value), property.PropertyType, options);
        }
        writer.WriteEndObject();
    }
}
