using KaraokeToolkit.Core.Execution;

namespace KaraokeToolkit.Mcp;

public sealed record McpOperationRegistration(OperationId Id, bool Included, bool ChangesFiles, string? ExclusionReason)
{
    public string Name => OperationCommandNames.Get(Id);
}

/// <summary>Explicit MCP exposure decisions, independent of CLI and desktop presentation.</summary>
public static class McpOperationCatalog
{
    public static IReadOnlyList<McpOperationRegistration> All { get; } = Array.AsReadOnly(Enum.GetValues<OperationId>().Select(Get).ToArray());
    public static McpOperationRegistration RequireIncluded(OperationId operation)
    {
        var registration = Get(operation);
        if (!registration.Included) throw new NotSupportedException($"MCP operation '{registration.Name}' is excluded: {registration.ExclusionReason} Call get_operations for available operations.");
        return registration;
    }
    public static McpOperationRegistration Get(OperationId operation)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unregistered MCP operation. Call get_operations for supported names.");
        try { return Describe(operation); }
        catch (System.Runtime.CompilerServices.SwitchExpressionException error)
        { throw new NotSupportedException($"Unregistered MCP operation '{operation}'. This installation has mismatched Core/MCP versions; rebuild/update the complete application and update McpOperationCatalog.", error); }
    }
#pragma warning disable CS8524
    private static McpOperationRegistration Describe(OperationId id) => id switch
    {
        OperationId.AudioGToBin => Include(id),
        OperationId.AudioGToZip => Include(id),
        OperationId.BinToAudioG => Include(id),
        OperationId.BinToZip => Include(id),
        OperationId.ZipToAudioG => Include(id),
        OperationId.ZipToBin => Include(id),
        OperationId.LosslessGToMp3G => Include(id),
        OperationId.KaraokeToVideo => Include(id),
        OperationId.ArchiveToZip => Include(id),
        OperationId.RepackZip => Include(id),
        OperationId.AddTrackIdSeparator => Include(id),
        OperationId.RemoveTrackIdSeparator => Include(id),
        OperationId.CleanUpFilenames => Include(id),
        OperationId.CreateCueAndTextFiles => Include(id),
        OperationId.ValidateKaraoke => Include(id, false),
        OperationId.TagKaraoke => Include(id),
        OperationId.PreviewTags => Include(id, false),
        OperationId.OrganizeFiles => Include(id),
        OperationId.OrganizeFolders => Include(id),
        OperationId.GroupByTrackId => Include(id),
        OperationId.FlattenOneLayer => Include(id),
        OperationId.FlattenAllLayers => Include(id),
        OperationId.RestoreQuarantined => Include(id),
        OperationId.QuarantineInvalid => Include(id),
        OperationId.DiscRip => Exclude(id, "Physical disc capture is experimental and is not exposed by MCP. Use the desktop or dedicated CLI disc commands."),
        OperationId.RenameFilesFromInternet => Exclude(id, "Online metadata lookup is deferred."),
        OperationId.RenameByPattern => Exclude(id, "Filename pattern renaming uses the desktop preview."),
        OperationId.PlayAudioG => Exclude(id, "Playback is deferred."),
        OperationId.CheckForUpdates => Exclude(id, "Update hosting is not enabled.")
    };
#pragma warning restore CS8524
    private static McpOperationRegistration Include(OperationId id, bool writes = true) => new(id, true, writes, null);
    private static McpOperationRegistration Exclude(OperationId id, string reason) => new(id, false, false, reason);
}
