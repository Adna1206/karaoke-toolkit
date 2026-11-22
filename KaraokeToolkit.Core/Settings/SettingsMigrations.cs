using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace KaraokeToolkit.Core.Settings;

internal static class SettingsMigrations
{
    private const string History = "AppliedSettingsMigrations";
    private static readonly Lazy<Migration[]> Bundled = new(ReadManifest);

    private sealed record Manifest(Migration[] Migrations);
    private sealed record Migration(string Id, Dictionary<string, JsonElement>? Set = null, string? DeleteAppDataFile = null);

    internal static bool ApplyValues(XElement root, bool existingProfile)
    {
        var pending = Pending(root).Where(migration => migration.Set != null)
            .Select(migration => (migration.Id, Updates: migration.Set!.Select(pair =>
                (Id: pair.Key, Value: SharedSettingCatalog.All.TryGetValue(pair.Key, out var setting) ? setting.Parse(pair.Value) :
                    throw new InvalidDataException($"Unknown setting in migration {migration.Id}: {pair.Key}"))).ToArray()))
            .ToArray();

        foreach (var migration in pending)
        {
            if (existingProfile)
                foreach (var (id, value) in migration.Updates) SettingsXml.Write(root, id, value);
            Record(root, migration.Id);
        }
        return pending.Length > 0;
    }

    internal static bool CleanupFiles(XElement root, IReadOnlyList<string> retiredOptionsFiles)
    {
        var pending = Pending(root).Where(migration => migration.DeleteAppDataFile != null).ToArray();
        foreach (var migration in pending)
        {
            // The manifest names the retired file. Core supplies the exact app-data paths.
            var targets = migration.DeleteAppDataFile switch
            {
                UserDataPaths.RetiredOptionsFileName => retiredOptionsFiles,
                _ => throw new InvalidDataException($"Unknown cleanup target in settings migration {migration.Id}.")
            };
            foreach (string path in targets)
            {
                try { File.Delete(path); }
                catch (DirectoryNotFoundException) { /* A missing former data directory is already clean. */ }
            }
            Record(root, migration.Id);
        }
        return pending.Length > 0;
    }

    private static IEnumerable<Migration> Pending(XElement root)
    {
        var applied = (root.Element(History)?.Elements("Id").Select(element => element.Value) ?? [])
            .ToHashSet(StringComparer.Ordinal);
        return Bundled.Value.Where(migration => !applied.Contains(migration.Id));
    }

    private static void Record(XElement root, string id) =>
        SettingsXml.Section(root, History).Add(new XElement("Id", id));

    private static Migration[] ReadManifest()
    {
        using var stream = typeof(SettingsMigrations).Assembly.GetManifestResourceStream("KaraokeToolkit.settings-migrations.json")
            ?? throw new InvalidDataException("The settings migration manifest is missing.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        var migrations = JsonSerializer.Deserialize<Manifest>(stream, options)?.Migrations
            ?? throw new InvalidDataException("The settings migration manifest is empty.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var migration in migrations)
        {
            if (migration == null || string.IsNullOrWhiteSpace(migration.Id) || !ids.Add(migration.Id))
                throw new InvalidDataException("Each settings migration needs a unique ID.");
            bool setsValues = migration.Set is { Count: > 0 } && migration.DeleteAppDataFile == null;
            bool deletesKnownFile = migration.DeleteAppDataFile == UserDataPaths.RetiredOptionsFileName && migration.Set == null;
            if (!setsValues && !deletesKnownFile)
                throw new InvalidDataException($"Invalid command in settings migration {migration.Id}.");
        }
        return migrations;
    }
}
