using System;
using System.Threading.Tasks;
using KaraokeToolkit.App.Features.AlphabeticalGrouping;
using KaraokeToolkit.Core.FileOperations;

namespace KaraokeToolkit.App.Features.Main;

public partial class MainViewModel
{
    internal Func<AlphabeticalGroupingViewModel, Task<AlphabeticalGroupingSubmission?>>? ConfigureAlphabeticalGroupingAsync { get; set; }

    private async Task ReviewAlphabeticalGroupingAsync()
    {
        IsBusy = true;
        try
        {
            if (ConfigureAlphabeticalGroupingAsync == null)
                throw new InvalidOperationException("The alphabetical grouping preview is unavailable.");
            var model = new AlphabeticalGroupingViewModel(CurrentInputs, IgnoreLeadingTheForFileOrganization, new AlphabeticalOrganizer(recovery));
            if (await ConfigureAlphabeticalGroupingAsync(model) is { } submission)
                await RunFileOperationAsync(alphabetical: submission);
        }
        catch (Exception error)
        {
            Status = "Could not open alphabetical grouping: " + error.Message;
            StatusOutcome = RunStatus.Failure;
        }
        finally { IsBusy = false; }
    }
}
