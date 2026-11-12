using static KaraokeToolkit.Core.Tests.Support.FileWorkflowFixture;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Validation;
using System.IO.Compression;
using System.Text.Json;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Core.Tests.Conversion;

public class SelectionConversionTests
{

    [Fact]
    public async Task SelectedZipDoesNotProcessItsNeighbours()
    {
        string folder = ZipFolder();
        File.Copy(Path.Combine(folder, "song.zip"), Path.Combine(folder, "other.zip"));
        var result = await Service().RunAsync(new(Action.Zip_To_AudioG, [Path.Combine(folder, "song.zip")], Options, OverwritePolicy.Fail));
        Assert.Equal(1, result.Succeeded);
        Assert.True(File.Exists(Path.Combine(folder, "song.cdg")));
        Assert.False(File.Exists(Path.Combine(folder, "other.cdg")));
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
    }
    [Fact]
    public void SelectingBothPairMembersAndTheirFolderDeduplicates()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        var plan = InputSelection.Plan(Action.AudioG_To_Zip, [folder, Path.Combine(folder, "song.wav"), Path.Combine(folder, "song.cdg")], true);
        Assert.Single(Assert.Single(plan).Sources);
    }
    [Fact]
    public async Task MultiFolderSelectionProducesOneCombinedReport()
    {
        string first = ZipFolder(), second = ZipFolder();
        var result = await Service().RunAsync(new(Action.Zip_To_AudioG, [first, second], Options, OverwritePolicy.Fail));
        Assert.Equal(2, result.Found);
        Assert.Equal(2, result.Succeeded);
        Assert.True(File.Exists(Path.Combine(first, "song.cdg")));
        Assert.True(File.Exists(Path.Combine(second, "song.cdg")));
    }
    [Fact]
    public void ExplicitWrongTypeCannotExpandIntoWholeFolder()
    {
        string folder = ZipFolder();
        Assert.Throws<ArgumentException>(() => InputSelection.Plan(Action.Bin_To_Zip, [Path.Combine(folder, "song.zip")], true));
        Assert.Throws<ArgumentException>(() => InputSelection.Plan(Action.Cleanup_Filenames, [Path.Combine(folder, "song.zip")], true));
    }
    [Fact]
    public async Task RetryUsesOnlyFailedSourcesAndSavedOptions()
    {
        string folder = ZipFolder();
        string bad = Path.Combine(folder, "bad.zip");
        File.WriteAllText(bad, "broken");
        var first = await Service().RunAsync(new(Action.Zip_To_AudioG, [folder], Options with
        {
            DeleteOldFiles = true
        }, OverwritePolicy.Fail));
        Assert.Equal(1, first.Succeeded);
        Assert.Equal(1, first.Failed);
        string completedHash = FileTransaction.Hash(Path.Combine(folder, "song.cdg"));
        var retry = Assert.Single(first.RetryRequests);
        Assert.Equal(new[] { bad }, retry.Inputs);
        Assert.True(retry.Options.DeleteOldFiles);
        File.Copy(Path.Combine(ZipFolder(), "song.zip"), bad, true);
        var second = await Service().RunAsync(retry);
        Assert.Equal(1, second.Succeeded);
        Assert.Equal(0, second.Failed);
        Assert.False(File.Exists(bad));
        Assert.Equal(completedHash, FileTransaction.Hash(Path.Combine(folder, "song.cdg")));
    }
    [Fact]
    public async Task FailedMetadataRetryDoesNotRequireDeletedMediaSource()
    {
        string folder = ZipFolder();
        string blocked = Path.Combine(folder, Path.GetFileName(folder) + ".txt");
        Directory.CreateDirectory(blocked);
        var result = await Service().RunAsync(new(Action.Zip_To_AudioG, [folder], Options with
        {
            DeleteOldFiles = true,
            AutoCreateCueText = true
        }, OverwritePolicy.Fail));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        var retry = Assert.Single(result.RetryRequests);
        Assert.Equal(Action.Create_Cue_And_Text_Files, retry.Action);
        Assert.False(File.Exists(Path.Combine(folder, "song.zip")));
        Directory.Delete(blocked);
        Assert.Equal(1, (await Service().RunAsync(retry)).Succeeded);
        Assert.True(File.Exists(blocked));
    }
    [Fact]
    public async Task CancellationBeforeSelectionDoesNotLeaveRecoveryJobs()
    {
        string folder = ZipFolder();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().RunAsync(new(Action.Zip_To_AudioG, [folder], Options, OverwritePolicy.Fail), token: cancel.Token));
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
    }
}
