using KaraokeToolkit.Core.Execution;

namespace KaraokeToolkit.Cli;

public sealed record CliOperationRegistration(OperationId OperationId, string? Name, string? ExclusionReason)
{
    public bool Exposed => Name != null;
    public bool DedicatedCommand { get; init; }
    internal bool MatchesName(string name) => Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Every Core identity needs an explicit CLI decision. Command names and
/// interface exclusions belong here, independent of GUI labels and Core capabilities.</summary>
public static class CliOperationCatalog
{
    public static IReadOnlyList<CliOperationRegistration> All { get; } = Array.AsReadOnly(Enum.GetValues<OperationId>().Select(Get).ToArray());
    public static IReadOnlyList<CliOperationRegistration> Exposed { get; } = Array.AsReadOnly(All.Where(o => o.Exposed).ToArray());

    public static CliOperationRegistration Resolve(string command)
    {
        if (!OperationCommandNames.TryResolve(command, out var operationId))
            throw new CliUsageException($"Unregistered CLI operation '{command}'. Run 'karaoke-toolkit operations' to list supported operations.");
        var registration = Get(operationId);
        if (!registration.Exposed)
            throw new CliUsageException($"CLI operation '{command}' is excluded: {registration.ExclusionReason} Run 'karaoke-toolkit operations' for available operations.");
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
        OperationId.ZipToBin => Include(id),
        OperationId.ZipToAudioG => Include(id),
        OperationId.BinToZip => Include(id),
        OperationId.BinToAudioG => Include(id),
        OperationId.AudioGToBin => Include(id),
        OperationId.AudioGToZip => Include(id),
        OperationId.AddTrackIdSeparator => Include(id),
        OperationId.RemoveTrackIdSeparator => Include(id),
        OperationId.CleanUpFilenames => Include(id),
        OperationId.CreateCueAndTextFiles => Include(id),
        OperationId.ArchiveToZip => Include(id),
        OperationId.LosslessGToMp3G => Include(id),
        OperationId.KmaToMp3G => Include(id),
        OperationId.KaraokeToVideo => Include(id),
        OperationId.RepackZip => Include(id),
        OperationId.ValidateKaraoke => Include(id),
        OperationId.TagKaraoke => Include(id),
        OperationId.NormalizeAudioGain => Include(id),
        OperationId.ChangeAudioKey => Include(id),
        OperationId.PreviewTags => Include(id),
        OperationId.OrganizeFiles => Include(id),
        OperationId.OrganizeFolders => Include(id),
        OperationId.GroupByTrackId => Include(id),
        OperationId.FlattenOneLayer => Include(id),
        OperationId.FlattenAllLayers => Include(id),
        OperationId.RestoreQuarantined => Include(id),
        OperationId.QuarantineInvalid => Include(id),
        OperationId.DiscRip
            => Include(id) with { DedicatedCommand = true },
        OperationId.RenameFilesFromInternet => Exclude(id, "Online metadata lookup is deferred."),
        OperationId.RenameByPattern => Exclude(id, "Filename pattern renaming uses the desktop preview."),
        OperationId.PlayAudioG => Exclude(id, "Playback is deferred."),
        OperationId.CheckForUpdates => Exclude(id, "Release hosting and update UI are not enabled.")
    };
#pragma warning restore CS8524

    private static CliOperationRegistration Include(OperationId id) => new(id, OperationCommandNames.Get(id), null);
    private static CliOperationRegistration Exclude(OperationId id, string reason) => new(id, null, reason);
}
