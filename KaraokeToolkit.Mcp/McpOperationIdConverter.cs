using System.Text.Json;
using System.Text.Json.Serialization;
using KaraokeToolkit.Core.Execution;
using ModelContextProtocol;

namespace KaraokeToolkit.Mcp;

/// <summary>Explain unknown operation identities to the MCP client before invocation.</summary>
public sealed class McpOperationIdConverter : JsonConverter<OperationId>
{
    public override OperationId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string identity = reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : reader.TokenType.ToString();
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<OperationId>(identity, out var value) &&
            Enum.IsDefined(value) && value.ToString() == identity) return value;
        throw new McpException($"Unregistered MCP operation '{identity}'. Use a named operation from get_operations. Numeric operation IDs are not supported.");
    }
    public override void Write(Utf8JsonWriter writer, OperationId value, JsonSerializerOptions options)
        => writer.WriteStringValue(McpOperationCatalog.Get(value).Id.ToString());
}
