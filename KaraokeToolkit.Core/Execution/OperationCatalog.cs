namespace KaraokeToolkit.Core.Execution;

/// <summary>Shared execution requirements, not GUI labels or CLI command names.</summary>
public sealed record OperationDescription(OperationId Id, string Description, bool IsConversion,
    bool RequiresFolders, bool CanRunUnattended, OperationSupport Support)
{
    public string Identifier => Id.ToString();
}

public static class OperationCatalog
{
    public static IReadOnlyList<OperationDescription> All { get; } = Array.AsReadOnly(Enum.GetValues<OperationId>().Select(Get).ToArray());

    public static OperationDescription Get(OperationId operation)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        return Describe(operation);
    }

    public static OperationDescription Get(string identifier) => All.SingleOrDefault(o => o.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Unknown operation: " + identifier);

    // Unknown numeric values are rejected by Get. Omitting the catch-all is
    // intentional: CS8509 is an error when a named enum entry is unaccounted for.
#pragma warning disable CS8524
    private static OperationDescription Describe(OperationId id) => id switch
    {
        OperationId.ZipToBin => Conversion(id, "Convert karaoke ZIP to BIN"),
        OperationId.ZipToAudioG => Conversion(id, "Extract matching audio and graphics from ZIP"),
        OperationId.BinToZip => Conversion(id, "Convert BIN tracks to karaoke ZIP"),
        OperationId.BinToAudioG => Conversion(id, "Extract and encode BIN audio with graphics"),
        OperationId.AudioGToBin => Conversion(id, "Combine matching audio and graphics into BIN"),
        OperationId.AudioGToZip => Conversion(id, "Package matching audio and graphics into ZIP"),
        OperationId.CleanUpFilenames => Conversion(id, "Clean up filenames in selected folders", folders: true),
        OperationId.CreateCueAndTextFiles => Conversion(id, "Create CUE sheets and text listings", folders: true),
        OperationId.ArchiveToZip => Conversion(id, "Convert supported non-ZIP archives to ZIP"),
        OperationId.LosslessGToMp3G => Conversion(id, "Encode lossless audio with graphics to MP3+G"),
        OperationId.KaraokeToVideo => Conversion(id, "Render karaoke files to video"),
        OperationId.RepackZip => Conversion(id, "Repack ZIP with selected compression settings"),
        OperationId.ValidateKaraoke => Local(id, "Check karaoke structure, pairing and ZIP integrity"),
        OperationId.TagKaraoke => Local(id, "Apply filename-derived metadata without re-encoding"),
        OperationId.PreviewTags => Local(id, "Preview filename-derived and existing tags without writing"),
        OperationId.OrganizeFiles => Local(id, "Organize files recursively into A-Z/# folders"),
        OperationId.OrganizeFolders => Local(id, "Organize immediate child folders into A-Z/# folders", folders: true),
        OperationId.GroupByTrackId => Local(id, "Organize karaoke files and folders into batches by track ID"),
        OperationId.FlattenOneLayer => Local(id, "Remove one layer of child folders", folders: true),
        OperationId.FlattenAllLayers => Local(id, "Move descendant files into selected roots", folders: true),
        OperationId.RestoreQuarantined => Local(id, "Restore a Damaged batch from its manifest"),
        OperationId.QuarantineInvalid => Local(id, "Validate and move eligible failures to Damaged"),
        OperationId.DiscRip => new(id, "Rip physical CD+G discs to track BINs, optionally using shared converters for Audio+G or ZIP; physical capture is hardware-unverified",
            false, false, true, OperationSupport.HardwareUnverified),
        OperationId.RenameFilesFromInternet => Deferred(id, "Online metadata lookup is deferred"),
        OperationId.PlayAudioG => Deferred(id, "Playback is deferred"),
        OperationId.CheckForUpdates => Deferred(id, "Update hosting and UI are not enabled")
    };
#pragma warning restore CS8524

    private static OperationDescription Conversion(OperationId id, string description, bool folders = false) => new(id, description, true, folders, true, OperationSupport.Supported);
    private static OperationDescription Local(OperationId id, string description, bool folders = false) => new(id, description, false, folders, true, OperationSupport.Supported);
    private static OperationDescription Deferred(OperationId id, string description) => new(id, description, false, false, false, OperationSupport.Deferred);
}
