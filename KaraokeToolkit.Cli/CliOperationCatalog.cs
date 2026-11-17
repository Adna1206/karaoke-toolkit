using KaraokeToolkit.Core.Execution;

namespace KaraokeToolkit.Cli;

public sealed record CliOperationRegistration(OperationId OperationId, string? Name, string? ExclusionReason)
{
    public bool Exposed => Name != null;
    public bool DedicatedCommand { get; init; }
}

/// <summary>Every Core identity needs an explicit CLI decision. Command names and
/// interface exclusions belong here, independent of GUI labels and Core capabilities.</summary>
public static class CliOperationCatalog
{
    public static IReadOnlyList<CliOperationRegistration> All { get; } = Array.AsReadOnly(Enum.GetValues<OperationId>().Select(Get).ToArray());
    public static IReadOnlyList<CliOperationRegistration> Exposed { get; } = Array.AsReadOnly(All.Where(o => o.Exposed).ToArray());

    public static CliOperationRegistration Resolve(string command)
    {
        var registration = All.SingleOrDefault(o =>
            string.Equals(o.Name, command, StringComparison.OrdinalIgnoreCase) || o.OperationId.ToString().Equals(command, StringComparison.OrdinalIgnoreCase));
        if (registration == null)
            throw new CliUsageException($"Unregistered CLI operation '{command}'. Run 'karaoke-toolkit operations' to list supported operations.");
        if (!registration.Exposed)
            throw new CliUsageException($"CLI operation '{registration.OperationId}' is excluded: {registration.ExclusionReason} Run 'karaoke-toolkit operations' for available operations.");
        return registration;
    }

    public static CliOperationRegistration Get(OperationId operation)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unregistered CLI operation. Run 'karaoke-toolkit operations' for supported names.");
        try { return Registration(operation); }
        catch (System.Runtime.CompilerServices.SwitchExpressionException error)
        { throw new CliUsageException($"Unregistered CLI operation '{operation}'. This installation has mismatched Core/CLI versions; rebuild/update the complete application and update CliOperationCatalog. {error.Message}"); }
    }

    // Get rejects undefined numbers. CS8509 is an error: adding a named Core enum
    // entry without deciding how CLI treats it must fail compilation.
#pragma warning disable CS8524
    private static CliOperationRegistration Registration(OperationId id) => id switch
    {
        OperationId.ZipToBin => Include(id, "zip-to-bin"),
        OperationId.ZipToAudioG => Include(id, "zip-to-audiog"),
        OperationId.BinToZip => Include(id, "bin-to-zip"),
        OperationId.BinToAudioG => Include(id, "bin-to-audiog"),
        OperationId.AudioGToBin => Include(id, "audiog-to-bin"),
        OperationId.AudioGToZip => Include(id, "audiog-to-zip"),
        OperationId.AddTrackIdSeparator => Include(id, "add-track-id-separator"),
        OperationId.RemoveTrackIdSeparator => Include(id, "remove-track-id-separator"),
        OperationId.CleanUpFilenames => Include(id, "cleanup-filenames"),
        OperationId.CreateCueAndTextFiles => Include(id, "create-cue-and-text-files"),
        OperationId.ArchiveToZip => Include(id, "archive-to-zip"),
        OperationId.LosslessGToMp3G => Include(id, "losslessg-to-mp3g"),
        OperationId.KaraokeToVideo => Include(id, "karaoke-to-video"),
        OperationId.RepackZip => Include(id, "repack-zip"),
        OperationId.ValidateKaraoke => Include(id, "validate"),
        OperationId.TagKaraoke => Include(id, "tag"),
        OperationId.PreviewTags => Include(id, "tag-preview"),
        OperationId.OrganizeFiles => Include(id, "organize-files"),
        OperationId.OrganizeFolders => Include(id, "organize-folders"),
        OperationId.GroupByTrackId => Include(id, "group-by-track-id"),
        OperationId.FlattenOneLayer => Include(id, "flatten-one-layer"),
        OperationId.FlattenAllLayers => Include(id, "flatten-all-layers"),
        OperationId.RestoreQuarantined => Include(id, "restore-quarantined-files"),
        OperationId.QuarantineInvalid => Include(id, "quarantine-invalid"),
        OperationId.DiscRip
            => new(id, "disc-rip", null) { DedicatedCommand = true },
        OperationId.RenameFilesFromInternet => Exclude(id, "Online metadata lookup is deferred."),
        OperationId.PlayAudioG => Exclude(id, "Playback is deferred."),
        OperationId.CheckForUpdates => Exclude(id, "Release hosting and update UI are not enabled.")
    };
#pragma warning restore CS8524

    private static CliOperationRegistration Include(OperationId id, string name) => new(id, name, null);
    private static CliOperationRegistration Exclude(OperationId id, string reason) => new(id, null, reason);
}
