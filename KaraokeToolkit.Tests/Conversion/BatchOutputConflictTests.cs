using System.IO.Compression;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Tests.Conversion;

public class BatchOutputConflictTests
{
    [Fact]
    public async Task OneReviewAfterConversionsHandlesMixedChoicesAndTheAutomaticListing()
    {
        string replace = SeedAudio();
        string skip = SeedAudio();
        string clear = SeedAudio();
        string replaceZip = ExistingZip(replace);
        string skipZip = ExistingZip(skip);
        string listing = Path.Combine(replace, "tracks.txt");
        File.WriteAllText(listing, "old listing");
        var recovery = new RecoveryStore(TestMediaFixture.NewFolder());
        var request = new ConversionRetry(OperationId.AudioGToZip, [replace, skip, clear],
            new() { TrackListingName = "tracks", AutoCreateCueText = true, DeleteOldFiles = true }, OverwritePolicy.Ask);
        int reviews = 0;

        var result = await new SelectionConversionService(recovery).RunAsync(request, resolveConflicts: (conflicts, _) =>
        {
            reviews++;
            Assert.Equal(3, conflicts.Count);
            Assert.True(File.Exists(Path.Combine(clear, "song.zip")));
            Assert.False(File.Exists(Path.Combine(clear, "song.wav")));
            Assert.Equal("old ZIP", File.ReadAllText(replaceZip));
            Assert.Equal("old ZIP", File.ReadAllText(skipZip));
            Assert.Equal("old listing", File.ReadAllText(listing));
            return Task.FromResult<IReadOnlyList<ConflictDecision>?>(conflicts.Select(conflict =>
                conflict.ExistingFiles.Contains(skipZip) ? ConflictDecision.Skip : ConflictDecision.Replace).ToArray());
        });

        Assert.Equal(1, reviews);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        using var zip = ZipFile.OpenRead(replaceZip);
        Assert.NotNull(zip.GetEntry("song.cdg"));
        Assert.Contains("song", File.ReadAllText(listing));
        Assert.False(File.Exists(Path.Combine(replace, "song.wav")));
        Assert.Equal("old ZIP", File.ReadAllText(skipZip));
        Assert.True(File.Exists(Path.Combine(skip, "song.wav")));
        Assert.Empty(recovery.Find());
    }

    [Fact]
    public async Task AChangedDestinationFailsSafelyWhileAnotherApprovedGroupCompletes()
    {
        string changed = SeedAudio();
        string ready = SeedAudio();
        string changedOutput = ExistingZip(changed);
        string readyOutput = ExistingZip(ready);
        var recovery = new RecoveryStore(TestMediaFixture.NewFolder());
        var request = new ConversionRetry(OperationId.AudioGToZip, [changed, ready],
            new() { AutoCreateCueText = false, DeleteOldFiles = true }, OverwritePolicy.Ask);

        var result = await new SelectionConversionService(recovery).RunAsync(request, resolveConflicts: (conflicts, _) =>
        {
            File.WriteAllText(changedOutput, "newer output");
            return Task.FromResult<IReadOnlyList<ConflictDecision>?>(conflicts.Select(_ => ConflictDecision.Replace).ToArray());
        });

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal("newer output", File.ReadAllText(changedOutput));
        Assert.True(File.Exists(Path.Combine(changed, "song.wav")));
        using var zip = ZipFile.OpenRead(readyOutput);
        Assert.NotNull(zip.GetEntry("song.cdg"));
        Assert.Empty(recovery.Find());
    }

    [Fact]
    public async Task CancellingReviewKeepsPublishedWorkAndDiscardsUnpublishedStaging()
    {
        string conflict = SeedAudio();
        string clear = SeedAudio();
        string existing = ExistingZip(conflict);
        var recovery = new RecoveryStore(TestMediaFixture.NewFolder());
        var request = new ConversionRetry(OperationId.AudioGToZip, [conflict, clear],
            new() { AutoCreateCueText = false, DeleteOldFiles = true }, OverwritePolicy.Ask);

        var result = await new SelectionConversionService(recovery).RunAsync(request, resolveConflicts: (conflicts, _) =>
        {
            Assert.Single(conflicts);
            return Task.FromResult<IReadOnlyList<ConflictDecision>?>(null);
        });

        Assert.True(result.Cancelled);
        Assert.Equal(1, result.Succeeded);
        Assert.True(File.Exists(Path.Combine(clear, "song.zip")));
        Assert.True(File.Exists(Path.Combine(conflict, "song.wav")));
        Assert.Equal("old ZIP", File.ReadAllText(existing));
        Assert.Empty(recovery.Find());
        Assert.Empty(Directory.GetDirectories(conflict, ".karaoke-job-*"));
    }

    [Fact]
    public async Task InterruptedReviewCanBeReopenedThroughRecovery()
    {
        string folder = SeedAudio();
        string existing = ExistingZip(folder);
        var recovery = new RecoveryStore(TestMediaFixture.NewFolder());
        var request = new ConversionRetry(OperationId.AudioGToZip, [folder],
            new() { AutoCreateCueText = false, DeleteOldFiles = true }, OverwritePolicy.Ask);

        await Assert.ThrowsAsync<IOException>(() => new SelectionConversionService(recovery).RunAsync(request,
            resolveConflicts: (_, _) => throw new IOException("Interrupted before applying choices.")));
        Assert.Equal("old ZIP", File.ReadAllText(existing));
        var job = Assert.Single(recovery.Find());
        int reviews = 0;
        var result = await new ConversionService(recovery).ResumeWithConflictReviewAsync(job, (conflicts, _) =>
        {
            reviews++;
            Assert.Contains(existing, Assert.Single(conflicts).ExistingFiles);
            return Task.FromResult<IReadOnlyList<ConflictDecision>?>([ConflictDecision.Replace]);
        });

        Assert.Equal(1, reviews);
        Assert.Equal(1, result.Succeeded);
        Assert.False(File.Exists(Path.Combine(folder, "song.wav")));
        Assert.Empty(recovery.Find());
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
    }

    private static string SeedAudio()
    {
        string path = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(path, OperationId.AudioGToBin, "keep");
        return path;
    }

    private static string ExistingZip(string folder)
    {
        string path = Path.Combine(folder, "song.zip");
        File.WriteAllText(path, "old ZIP");
        return path;
    }
}
