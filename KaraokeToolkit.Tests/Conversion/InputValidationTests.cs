using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.Conversion;

public class InputValidationTests
{

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalInputPassRunsOnlyWhenEnabled(bool enabled)
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.ZipToBin, "keep");
        var progress = new List<string>();
        var reporter = new SelectionConversionService.InlineProgress(p => progress.Add(p.Message));
        var result = await new ConversionService(new(TestMediaFixture.NewFolder())).RunAsync(
            new(Action.ZipToAudioG, folder, new()
            {
                AutoCreateCueText = false,
                DeleteOldFiles = false
            }),
            progress: reporter, validateBeforeConversion: enabled);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(enabled, progress.Any(p => p.StartsWith("Validating input:")));
    }

    [Fact]
    public async Task ValidationFailureKeepsSourcesAndThePreferenceForRetry()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioGToBin, "short-graphics");
        var before = TestMediaFixture.Snapshot(folder);
        var request = new ConversionRetry(Action.AudioGToZip, [folder], new() { AutoCreateCueText = false }, OverwritePolicy.Fail) { ValidateBeforeConversion = true };
        var result = await new SelectionConversionService(new(TestMediaFixture.NewFolder())).RunAsync(request);
        Assert.Equal(1, result.Failed);
        Assert.StartsWith("Input validation (Damaged)", result.Files[0].Message);
        Assert.True(Assert.Single(result.RetryRequests).ValidateBeforeConversion);
        Assert.Equal(before, TestMediaFixture.Snapshot(folder));
    }

}
