using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.App.Features.Main;

public enum RunStatus { Neutral, Success, Warning, Failure }

internal static class RunStatusSummary
{
    // Cancellation is intentional. Mixed results and skipped work need review;
    // an empty run must not imply that files were successfully processed.
    internal static RunStatus From(ConversionResult result) => From(result.Cancelled, result.Succeeded, result.Failed, result.Skipped);

    internal static RunStatus From(bool cancelled, int succeeded, int failed, int skipped) => cancelled ? RunStatus.Neutral
        : failed > 0 && succeeded == 0 ? RunStatus.Failure
        : failed > 0 || skipped > 0 || succeeded == 0 ? RunStatus.Warning
        : RunStatus.Success;
}
