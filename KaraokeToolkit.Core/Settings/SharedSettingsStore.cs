using System.Text.Json;

namespace KaraokeToolkit.Core.Settings;

public enum SettingsClient { Cli, Mcp }

/// <summary>The common processing profile. Reads apply pending settings changes in memory.</summary>
public sealed class SharedSettingsStore
{
    private readonly SettingsStore store;

    public SharedSettingsStore(string path) : this(new SettingsStore(path, persistRenameBins: true, applyReleaseMigrations: true)) { }

    internal SharedSettingsStore(SettingsStore store) => this.store = store;

    public static SharedSettingsStore ForCurrentUser() => new(UserDataPaths.SharedSettingsPath());

    public SharedSettingsSnapshot Load() => store.ReadSnapshot();

    /// <summary>Initializes the profile and commits any pending migrations.</summary>
    public void ApplyMigrations() => store.UpdateDocument();

    public IReadOnlyList<SettingValue> List(SettingsClient client)
    {
        var snapshot = Load();
        return SharedSettingCatalog.All.Values.Where(setting => CanAccess(setting, client))
            .Select(setting => setting.Describe(snapshot)).ToArray();
    }

    public SettingValue Get(string id, SettingsClient client) => Find(id, client).Describe(Load());

    public SettingValue Set(string id, string value, SettingsClient client)
    {
        var setting = Find(id, client);
        return Write(setting, setting.Parse(value));
    }

    public SettingValue Set(string id, JsonElement value, SettingsClient client)
    {
        var setting = Find(id, client);
        return Write(setting, setting.Parse(value));
    }

    private SettingValue Write(SettingDefinition setting, object value)
    {
        SettingValue? result = null;
        store.UpdateDocument(root =>
        {
            SettingsXml.Write(root, setting.Id, value);
            result = setting.Describe(SettingsXml.Read(root));
        });
        return result!;
    }

    private static SettingDefinition Find(string id, SettingsClient client) =>
        SharedSettingCatalog.All.TryGetValue(id, out var setting) && CanAccess(setting, client)
            ? setting : throw new ArgumentException("Unknown or unavailable setting: " + id);

    private static bool CanAccess(SettingDefinition setting, SettingsClient client) =>
        (client switch
        {
            SettingsClient.Cli => setting.Cli,
            SettingsClient.Mcp => setting.Mcp,
            _ => throw new ArgumentOutOfRangeException(nameof(client))
        }) == SettingAccess.ReadWrite;
}
