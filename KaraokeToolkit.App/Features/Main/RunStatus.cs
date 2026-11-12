using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.App.Features.Main;

public enum RunStatus { Neutral, Success, Warning, Failure }

internal static class RunStatusSummary
{
    // Cancellation is intentional. Mixed results and skipped work need review;
    // an empty run must not imply that files were successfully processed.
    internal static RunStatus From(ConversionResult result) => result.Cancelled ? RunStatus.Neutral
        : result.Failed > 0 && result.Succeeded == 0 ? RunStatus.Failure
        : result.Failed > 0 || result.Skipped > 0 || result.Succeeded == 0 ? RunStatus.Warning
        : RunStatus.Success;
}
