using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Reports;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Validation;
using System.IO.Compression;
using System.Text.Json;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.App.Main;

public class ValidationWorkflowTests
{
    [Fact]
    public async Task GuiValidationProducesAnActionableReportWithoutChangingFiles()
    {
        string folder = TestMediaFixture.NewFolder();
        File.WriteAllText(Path.Combine(folder, "bad.bin"), "bad");
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "options.xmls"));
        model.SetInputs([folder]);
        var footerProgress = new List<bool>();
        model.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(MainViewModel.ShowFooterProgress))
                footerProgress.Add(model.ShowFooterProgress);
        };
        await model.ValidateCommand.ExecuteAsync(null);
        Assert.Equal(new[] { true, false }, footerProgress);
        Assert.True(model.HasReport);
        Assert.False(model.IsBusy);
        Assert.False(model.CanRetry);
        Assert.Equal("Damaged", Assert.Single(model.LastReport!.Entries).Status);
        Assert.True(Assert.Single(new ResultsViewModel(model).Rows).CanMove);
        Assert.Equal("bad", File.ReadAllText(Path.Combine(folder, "bad.bin")));
    }

}
