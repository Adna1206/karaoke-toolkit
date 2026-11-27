using KaraokeToolkit.Core.Execution;
using static KaraokeToolkit.Tests.Support.FileWorkflowFixture;
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

namespace KaraokeToolkit.Tests.Conversion;

public class SelectionConversionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledScanLeavesNoRecoveryAndFreshRunReportsTheBatchTotal(bool selection)
    {
        string folder = ZipFolder();
        var recovery = new RecoveryStore(TestMediaFixture.NewFolder());
        using var cancellation = new CancellationTokenSource();
        var updates = new List<ConversionProgress>();
        var reporter = new SynchronousProgress<ConversionProgress>(updates.Add);

        Task<ConversionResult> Run(IProgress<ConversionProgress> progress, CancellationToken token) => selection
            ? new SelectionConversionService(recovery).RunAsync(new(Action.ZipToAudioG, [folder], Options, OverwritePolicy.Fail), progress, token)
            : new ConversionService(recovery).RunAsync(new(Action.ZipToAudioG, folder, Options), progress: progress, cancellationToken: token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(reporter, cancellation.Token));
        Assert.Empty(updates);
        Assert.False(File.Exists(Path.Combine(folder, "song.cdg")));
        Assert.Empty(recovery.Find());

        var result = await Run(reporter, default);
        Assert.Equal(1, result.Succeeded);
        var ready = updates[0];
        Assert.False(ready.IsIndeterminate);
        Assert.Equal(1, ready.Found);
        Assert.Equal(0, ready.Completed);
    }

    [Fact]
    public void CancellationBetweenEntriesStopsBeforeTheNextRead()
    {
        using var cancellation = new CancellationTokenSource();
        using var scan = InputSelection.ScanEntries(Entries(), cancellation.Token).GetEnumerator();
        Assert.True(scan.MoveNext());
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => scan.MoveNext());

        static IEnumerable<string> Entries()
        {
            yield return "first";
            throw new InvalidOperationException("The next entry should not be read.");
        }
    }

    [Fact]
    public async Task ResumeUsesTheSavedSourcesWithoutFreshDiscovery()
    {
        string folder = ZipFolder();
        string source = Path.Combine(folder, "song.zip");
        var recovery = new RecoveryStore(TestMediaFixture.NewFolder());
        using (new RunSession(recovery, new(Action.ZipToAudioG, folder, Options with { RecurseFolders = true }),
                   OverwritePolicy.Fail, [source])) { }
        string laterFolder = Directory.CreateDirectory(Path.Combine(folder, "Later")).FullName;
        File.Copy(source, Path.Combine(laterFolder, "extra.zip"));
        var updates = new List<ConversionProgress>();

        var result = await new ConversionService(recovery).ResumeAsync(Assert.Single(recovery.Find()),
            new SynchronousProgress<ConversionProgress>(updates.Add));

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(source, Assert.Single(result.Files).Source);
        Assert.DoesNotContain(updates, update => update.IsIndeterminate);
        Assert.False(File.Exists(Path.Combine(laterFolder, "extra.cdg")));
    }

    [Theory]
    [InlineData(false, FileCheckTiming.BeforeBatch, null)]
    [InlineData(false, FileCheckTiming.BeforeItem, null)]
    [InlineData(true, FileCheckTiming.BeforeBatch, null)]
    [InlineData(true, FileCheckTiming.BeforeItem, null)]
    [InlineData(true, FileCheckTiming.BeforeBatch, RecoveryVerificationMode.Sha256)]
    [InlineData(true, FileCheckTiming.BeforeBatch, RecoveryVerificationMode.MetadataOnly)]
    [InlineData(true, FileCheckTiming.BeforeBatch, RecoveryVerificationMode.None)]
    [InlineData(true, FileCheckTiming.BeforeItem, RecoveryVerificationMode.MetadataOnly)]
    [InlineData(true, FileCheckTiming.BeforeItem, RecoveryVerificationMode.None)]
    public async Task CheckTimingControlsWhetherAnEditToAWaitingAudioCompanionIsAccepted(
        bool selection, FileCheckTiming timing, RecoveryVerificationMode? mode)
    {
        string root = TestMediaFixture.NewFolder();
        string first = Path.Combine(root, "A"), second = Path.Combine(root, "B");
        TestMediaFixture.Seed(first, Action.AudioGToBin, "keep");
        TestMediaFixture.Seed(second, Action.AudioGToBin, "keep");
        string waiting = Path.Combine(second, "song.wav");
        DateTime originalWriteTime = File.GetLastWriteTimeUtc(waiting);
        var expectedMode = mode ?? RecoveryVerificationMode.XxHash128;
        byte[] modified = File.ReadAllBytes(waiting);
        modified[^1] ^= 1;
        bool edited = false;
        var progress = new SynchronousProgress<ConversionProgress>(update =>
        {
            if (!edited && update.ActivityLabel == "Processing")
            {
                var checkpoint = RunSession.Current.Value!.Data;
                Assert.Equal(expectedMode, checkpoint.VerificationMode);
                if (timing == FileCheckTiming.BeforeBatch)
                {
                    var files = checkpoint.BatchInputs![update.ActivitySource!].Files;
                    Assert.All(files, file => Assert.Equal(RecoveryFingerprint.Hash(file.Path, expectedMode), file.Hash));
                }
                File.WriteAllBytes(waiting, modified);
                File.SetLastWriteTimeUtc(waiting, originalWriteTime.AddMinutes(1));
                edited = true;
            }
        });
        var recovery = new RecoveryStore(TestMediaFixture.NewFolder());
        var options = Options with { RecurseFolders = true, DeleteOldFiles = false, AutoCreateCueText = false };

        var request = new OperationRequest(Action.AudioGToZip, [first, second]) { Options = options, CheckTiming = timing };
        if (mode is { } explicitMode) request = request with { RecoveryVerification = explicitMode };
        var result = selection
            ? (await new OperationExecutionService(recovery).ExecuteAsync(request, progress)).Report
            : CompletionReport.FromConversion("Test", await new ConversionService(recovery).RunAsync(
                new(Action.AudioGToZip, root, options), progress: progress, checkTiming: timing));

        Assert.True(edited);
        Assert.True(File.Exists(Path.Combine(first, "song.zip")));
        var waitingResult = Assert.Single(result.Entries, entry => entry.Source == Path.Combine(second, "song.cdg"));
        if (timing == FileCheckTiming.BeforeBatch && expectedMode != RecoveryVerificationMode.None)
        {
            Assert.Equal("Failed", waitingResult.Status);
            Assert.Contains("changed since the batch started", waitingResult.Message);
            Assert.False(File.Exists(Path.Combine(second, "song.zip")));
        }
        else
        {
            Assert.Equal("Succeeded", waitingResult.Status);
            using var archive = ZipFile.OpenRead(Path.Combine(second, "song.zip"));
            using var audio = archive.GetEntry("song.wav")!.Open();
            using var copy = new MemoryStream();
            await audio.CopyToAsync(copy);
            Assert.Equal(modified, copy.ToArray());
        }
        Assert.Empty(recovery.Find());
    }

    [Fact]
    public async Task SelectedZipDoesNotProcessItsNeighbours()
    {
        string folder = ZipFolder();
        File.Copy(Path.Combine(folder, "song.zip"), Path.Combine(folder, "other.zip"));
        var result = await Service().RunAsync(new(Action.ZipToAudioG, [Path.Combine(folder, "song.zip")], Options, OverwritePolicy.Fail));
        Assert.Equal(1, result.Succeeded);
        Assert.True(File.Exists(Path.Combine(folder, "song.cdg")));
        Assert.False(File.Exists(Path.Combine(folder, "other.cdg")));
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
    }
    [Fact]
    public void SelectingBothPairMembersAndTheirFolderDeduplicates()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioGToBin, "keep");
        var plan = InputSelection.Plan(Action.AudioGToZip, [folder, Path.Combine(folder, "song.wav"), Path.Combine(folder, "song.cdg")], true);
        Assert.Single(Assert.Single(plan).Sources);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MultiFolderSelectionProducesOneCombinedReport(bool continueBatch)
    {
        string first = ZipFolder(), second = ZipFolder();
        var result = await Service().RunAsync(new(Action.ZipToAudioG, [first, second], Options, OverwritePolicy.Fail)
            { ContinueBatchAfterRecovery = continueBatch });
        Assert.Equal(2, result.Found);
        Assert.Equal(2, result.Succeeded);
        Assert.True(File.Exists(Path.Combine(first, "song.cdg")));
        Assert.True(File.Exists(Path.Combine(second, "song.cdg")));
    }
    [Fact]
    public void ExplicitWrongTypeCannotExpandIntoWholeFolder()
    {
        string folder = ZipFolder();
        Assert.Throws<ArgumentException>(() => InputSelection.Plan(Action.BinToZip, [Path.Combine(folder, "song.zip")], true));
        Assert.Throws<ArgumentException>(() => InputSelection.Plan(Action.CleanUpFilenames, [Path.Combine(folder, "song.zip")], true));
    }
    [Fact]
    public async Task RetryUsesOnlyFailedSourcesAndSavedOptions()
    {
        string folder = ZipFolder();
        string bad = Path.Combine(folder, "bad.zip");
        File.WriteAllText(bad, "broken");
        var first = await Service().RunAsync(new(Action.ZipToAudioG, [folder], Options with
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
        var result = await Service().RunAsync(new(Action.ZipToAudioG, [folder], Options with
        {
            DeleteOldFiles = true,
            AutoCreateCueText = true
        }, OverwritePolicy.Fail));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        var retry = Assert.Single(result.RetryRequests);
        Assert.Equal(Action.CreateCueAndTextFiles, retry.Action);
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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().RunAsync(new(Action.ZipToAudioG, [folder], Options, OverwritePolicy.Fail), token: cancel.Token));
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
    }
}
