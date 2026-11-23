using System.Text.Json.Serialization;

namespace KaraokeToolkit.Core.Execution;

/// <summary>Built-in identities. Persist names; numeric values and ordering are internal details.</summary>
[JsonConverter(typeof(OperationIdJsonConverter))]
public enum OperationId
{
    AudioGToBin,
    AudioGToZip,
    BinToAudioG,
    BinToZip,
    ZipToAudioG,
    ZipToBin,
    LosslessGToMp3G,
    KaraokeToVideo,
    ArchiveToZip,
    RepackZip,
    CleanUpFilenames,
    CreateCueAndTextFiles,
    ValidateKaraoke,
    TagKaraoke,
    PreviewTags,
    OrganizeFiles,
    OrganizeFolders,
    GroupByTrackId,
    FlattenOneLayer,
    FlattenAllLayers,
    RestoreQuarantined,
    QuarantineInvalid,
    DiscRip,
    RenameFilesFromInternet,
    PlayAudioG,
    AddTrackIdSeparator,
    RemoveTrackIdSeparator,
    CheckForUpdates,
    RenameByPattern
}
