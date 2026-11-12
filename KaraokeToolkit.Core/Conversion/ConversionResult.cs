namespace KaraokeToolkit.Core.Conversion;

public enum OverwritePolicy
{
    Fail, Skip, Replace, Ask
}
public enum ConversionStatus
{
    Succeeded, Skipped, Failed, Cancelled
}
/// <summary>One source group (a BIN, ZIP, or matching audio/CDG pair) and its committed outputs.</summary>
public sealed record ConversionFileResult(string Source, ConversionStatus Status, IReadOnlyList<string> Outputs, string Message)
{
    public bool IsMetadata
    {
        get; init;
    }
}
public sealed record ConversionResult(IReadOnlyList<ConversionFileResult> Files, bool Cancelled)
{
    public IReadOnlyList<ConversionRetry> RetryRequests { get; init; } = [];
    public int Found
    {
        get; init;
    }
    public TimeSpan Elapsed
    {
        get; init;
    }
    public int Succeeded => Files.Count(x => x.Status == ConversionStatus.Succeeded);
    public int Failed => Files.Count(x => x.Status == ConversionStatus.Failed);
    public int Skipped => Files.Count(x => x.Status == ConversionStatus.Skipped);
}
/// <summary>Percentages are bounded 0..100; Completed counts attempted source groups.</summary>
public sealed record ConversionProgress(int FilePercent, int TotalPercent, int Found, int Completed, string Message, string Elapsed);


