namespace KaraokeToolkit.Core.Execution;

/// <summary>Shared CLI and MCP command vocabulary. Adapters decide which commands they expose.</summary>
public static class OperationCommandNames
{
    private static readonly IReadOnlyDictionary<string, OperationId> Ids =
        Enum.GetValues<OperationId>().ToDictionary(Get, StringComparer.OrdinalIgnoreCase);

    public static bool TryResolve(string name, out OperationId id) => Ids.TryGetValue(name, out id);

    public static string Get(OperationId id)
    {
        if (!Enum.IsDefined(id)) throw new ArgumentOutOfRangeException(nameof(id));
        return Name(id);
    }

#pragma warning disable CS8524
    private static string Name(OperationId id) => id switch
    {
        OperationId.AudioGToBin => "audiog-to-bin",
        OperationId.AudioGToZip => "audiog-to-zip",
        OperationId.BinToAudioG => "bin-to-audiog",
        OperationId.BinToZip => "bin-to-zip",
        OperationId.ZipToAudioG => "zip-to-audiog",
        OperationId.ZipToBin => "zip-to-bin",
        OperationId.LosslessGToMp3G => "lossless-audiog-to-mp3g",
        OperationId.KmaToMp3G => "kma-to-mp3g",
        OperationId.KaraokeToVideo => "karaoke-to-video",
        OperationId.ArchiveToZip => "archives-to-zip",
        OperationId.RepackZip => "repack-zip-for-compatibility",
        OperationId.CleanUpFilenames => "clean-up-filenames",
        OperationId.CreateCueAndTextFiles => "create-cue-txt-files",
        OperationId.ValidateKaraoke => "validate-karaoke-files",
        OperationId.TagKaraoke => "tag-karaoke-files",
        OperationId.NormalizeAudioGain => "normalize-audio-gain",
        OperationId.ChangeAudioKey => "change-audio-key",
        OperationId.PreviewTags => "preview-tags",
        OperationId.OrganizeFiles => "group-by-alphabet-files",
        OperationId.OrganizeFolders => "group-by-alphabet-folders",
        OperationId.GroupByTrackId => "group-by-track-id",
        OperationId.FlattenOneLayer => "flatten-one-folder-level",
        OperationId.FlattenAllLayers => "flatten-all-folder-levels",
        OperationId.RestoreQuarantined => "restore-quarantined-files",
        OperationId.QuarantineInvalid => "quarantine-invalid",
        OperationId.DiscRip => "rip-cdg",
        OperationId.RenameFilesFromInternet => "rename-files-from-internet",
        OperationId.PlayAudioG => "play-audiog",
        OperationId.AddTrackIdSeparator => "add-track-id-separator",
        OperationId.RemoveTrackIdSeparator => "remove-track-id-separator",
        OperationId.CheckForUpdates => "check-for-updates",
        OperationId.RenameByPattern => "rename-by-filename-pattern"
    };
#pragma warning restore CS8524
}
