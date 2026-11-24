using KaraokeToolkit.Core.Conversion;
using System.Linq;

namespace KaraokeToolkit.App.Features.Main;

public enum RunStatus { Neutral, Success, Warning, Failure }

internal static class RunStatusSummary
{
    // Recorded failures remain visible even when other items completed or the run was cancelled.
    internal static RunStatus From(ConversionResult result)
    {
        var status = From(result.Cancelled, result.Succeeded, result.Failed, result.Skipped);
        return status == RunStatus.Success && result.Files.Any(file => file.Warning != null) ? RunStatus.Warning : status;
    }

    internal static RunStatus From(bool cancelled, int succeeded, int failed, int skipped) => failed > 0 ? RunStatus.Failure
        : cancelled ? RunStatus.Neutral
        : skipped > 0 || succeeded == 0 ? RunStatus.Warning
        : RunStatus.Success;
}
