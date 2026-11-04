using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Tagging;

namespace KaraokeToolkit.Core.Execution;

/// <summary>General per-invocation contract. Interface preferences and command names
/// are not part of the execution request. No desktop settings are read or modified.</summary>
public sealed record OperationRequest(OperationId OperationId, IReadOnlyList<string> Inputs)
{
    public ConversionOptions Options { get; init; } = new() { DeleteOldFiles = false };
    public OverwritePolicy OverwritePolicy { get; init; } = OverwritePolicy.Fail;
    public bool ValidateBeforeConversion { get; init; }
    public bool IgnoreLeadingThe { get; init; }
    public string TaggingPattern { get; init; } = FilenameTagPattern.Default;
    public TaggingOptions Tagging { get; init; } = new();
}
