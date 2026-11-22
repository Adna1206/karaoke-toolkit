using System.Text.Json;

namespace KaraokeToolkit.Core.Settings;

public enum SettingAccess { Hidden, ReadWrite }

public sealed record SettingValue(string Id, string Type, object Default, object Value,
    IReadOnlyList<string> Choices, SettingAccess Cli, SettingAccess Mcp);

internal abstract class SettingDefinition
{
    public abstract string Id { get; }
    public abstract SettingAccess Cli { get; }
    public abstract SettingAccess Mcp { get; }
    internal abstract SettingValue Describe(SharedSettingsSnapshot snapshot);
    internal abstract object Parse(string text);
    internal abstract object Parse(JsonElement value);
}

internal sealed class SettingDefinition<T>(
    string id, T defaultValue, SettingAccess cli, SettingAccess mcp,
    Func<SharedSettingsSnapshot, T> read, Func<string, T> parse,
    IReadOnlyList<string>? choices = null)
    : SettingDefinition where T : notnull
{
    public override string Id => id;
    public override SettingAccess Cli => cli;
    public override SettingAccess Mcp => mcp;

    internal override SettingValue Describe(SharedSettingsSnapshot snapshot) =>
        new(Id, typeof(T).IsEnum ? "enum" : typeof(T) == typeof(bool) ? "boolean" :
            typeof(T) == typeof(int) ? "integer" : "string", defaultValue, read(snapshot), choices ?? [], Cli, Mcp);

    internal override object Parse(string text)
    {
        try { return parse(text); }
        catch (ArgumentException error) { throw new ArgumentException($"Invalid value for {Id}. {error.Message}", error); }
    }

    internal override object Parse(JsonElement value)
    {
        string text = value.ValueKind switch
        {
            JsonValueKind.String when typeof(T) == typeof(string) || typeof(T).IsEnum => value.GetString()!,
            JsonValueKind.True or JsonValueKind.False when typeof(T) == typeof(bool) => value.GetRawText(),
            JsonValueKind.Number when typeof(T) == typeof(int) => value.GetRawText(),
            _ => throw new ArgumentException($"Invalid JSON value type for {Id}.")
        };
        return Parse(text);
    }
}
