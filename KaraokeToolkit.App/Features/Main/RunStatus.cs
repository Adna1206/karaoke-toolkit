using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.App.Features.Main;

public enum RunStatus { Neutral, Success, Warning, Failure }

internal static class RunStatusSummary
{
    // Recorded failures remain visible even when other items completed or the run was cancelled.
    internal static RunStatus From(ConversionResult result) => From(result.Cancelled, result.Succeeded, result.Failed, result.Skipped);

    internal static RunStatus From(bool cancelled, int succeeded, int failed, int skipped) => failed > 0 ? RunStatus.Failure
        : cancelled ? RunStatus.Neutral
        : skipped > 0 || succeeded == 0 ? RunStatus.Warning
        : RunStatus.Success;
}
