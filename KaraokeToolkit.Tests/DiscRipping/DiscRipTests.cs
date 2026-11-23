using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Features.DiscRipping;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.DiscRipping;
using KaraokeToolkit.Core.Settings;
using System.IO.Compression;
namespace KaraokeToolkit.Tests.DiscRipping;

public class DiscRipTests
{
    [Theory]
    [InlineData(RipFormat.Bin, true, true)]
    [InlineData(RipFormat.Bin, false, false)]
    [InlineData(RipFormat.AudioG, true, false)]
    [InlineData(RipFormat.Zip, true, false)]
    public async Task RippingUsesSharedMetadataToggle(RipFormat format, bool enabled, bool cue)
    {
        var reader = new FakeReader();
        string folder = TestMediaFixture.NewFolder();
        var request = Request(reader, folder, format) with { Options = Options with { AutoCreateCueText = enabled, AudioEncoder = AudioEncoder.WAV } };
        var result = await new DiscRipService(reader).RunAsync(request, OverwritePolicy.Fail);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(enabled, Directory.GetFiles(folder, "*.txt").Length == 1);
        Assert.Equal(cue, Directory.GetFiles(folder, "*.cue").Length == 1);
        if (enabled) Assert.Contains("DISC-01", File.ReadAllText(Directory.GetFiles(folder, "*.txt").Single()));
    }

    [Theory]
    [InlineData(ConflictDecision.Skip)]
    [InlineData(ConflictDecision.Replace)]
    public async Task RippingMetadataPromptsBeforeReplacingExistingListing(ConflictDecision decision)
    {
        var reader = new FakeReader();
        string folder = TestMediaFixture.NewFolder();
        string listing = Path.Combine(folder, new DirectoryInfo(folder).Name + ".txt");
        File.WriteAllText(listing, "original listing");
        int prompts = 0;
        var result = await new DiscRipService(reader).RunAsync(Request(reader, folder) with { Options = Options with { AutoCreateCueText = true } },
            OverwritePolicy.Ask, resolve: (conflict, _) =>
            {
                prompts++;
                Assert.Contains(listing, conflict.ExistingFiles);
                return Task.FromResult(decision);
            });
        Assert.Equal(1, prompts);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(decision == ConflictDecision.Skip, File.ReadAllText(listing) == "original listing");
    }

    internal sealed class FakeReader : IDiscReader
    {
        internal OpticalDisc Disc = new(0, "Test drive", true, "disc-A", [new(1, 150 * 2352), new(2, 150 * 2352)]);
        internal List<int> ReadTracks = [];
        internal string Mode = "ok";
        internal CancellationTokenSource? Cancel;
        public Task<IReadOnlyList<OpticalDisc>> ScanAsync(CancellationToken token) => Mode == "scan-error" ? throw new IOException("Reader unavailable") : Task.FromResult<IReadOnlyList<OpticalDisc>>(Mode == "no-drive" ? [] : [Disc]);
        public async Task ReadTrackAsync(OpticalDisc disc, DiscTrack track, string stagedBin, IProgress<int>? progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ReadTracks.Add(track.Number);
            if (Mode == "changed") throw new IOException("Disc changed. Refresh the disc.");
            byte[] bin = new byte[track.BinBytes];
            for (int sector = 0; sector < 150; sector++)
                for (int i = 0; i < 96; i++) bin[sector * 2448 + 2352 + i] = (byte)(i % 64);
            await File.WriteAllBytesAsync(stagedBin, Mode == "short" ? bin[..^1] : bin, token);
            if (Mode == "read-error") throw new IOException("Drive read failed");
            if (Mode == "cancel") Cancel!.Cancel();
            token.ThrowIfCancellationRequested();
            progress?.Report(100);
        }
    }
    static ConversionOptions Options => new() { ExperimentalDiscRipping = true, AutoCreateCueText = false };
    static DiscRipRequest Request(FakeReader reader, string folder, RipFormat format = RipFormat.Bin) => new(reader.Disc, [1], folder, "DISC", format, Options);

    [Theory]
    [InlineData(RipFormat.Bin, "bin")]
    [InlineData(RipFormat.AudioG, "mp3")]
    [InlineData(RipFormat.Zip, "zip")]
    public async Task FakeSectorsRunThroughRealOutputPipeline(RipFormat format, string extension)
    {
        var reader = new FakeReader(); string folder = TestMediaFixture.NewFolder();
        var result = await new DiscRipService(reader).RunAsync(Request(reader, folder, format), OverwritePolicy.Fail);
        Assert.True(result.Succeeded == 1, string.Join(";", result.Files.Select(f => f.Message)));
        Assert.Equal(new[] { 1 }, reader.ReadTracks);
        Assert.True(File.Exists(Path.Combine(folder, "DISC-01." + extension)));
        if (format == RipFormat.Bin) Assert.Equal(150 * 2448, new FileInfo(Path.Combine(folder, "DISC-01.bin")).Length);
        if (format == RipFormat.AudioG) Assert.Equal(150 * 96, new FileInfo(Path.Combine(folder, "DISC-01.cdg")).Length);
        if (format == RipFormat.Zip)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(folder, "DISC-01.zip"));
            Assert.Equal(2, zip.Entries.Count);
            Assert.Equal(150 * 96, zip.GetEntry("DISC-01.cdg")!.Length);
        }
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
    }
    [Theory]
    [InlineData(AudioEncoder.WMA, "wma")]
    [InlineData(AudioEncoder.WAV, "wav")]
    [InlineData(AudioEncoder.FLAC, "flac")]
    [InlineData(AudioEncoder.AAC, "m4a")]
    public async Task RippedTracksUseChosenAudioFormat(AudioEncoder encoder, string extension)
    {
        var reader = new FakeReader(); string folder = TestMediaFixture.NewFolder();
        var request = Request(reader, folder, RipFormat.AudioG) with { Options = Options with { AudioEncoder = encoder } };
        var result = await new DiscRipService(reader).RunAsync(request, OverwritePolicy.Fail);
        Assert.Equal(1, result.Succeeded);
        Assert.True(File.Exists(Path.Combine(folder, "DISC-01." + extension)));
        Assert.True(File.Exists(Path.Combine(folder, "DISC-01.cdg")));
    }
    [Theory]
    [InlineData("short")]
    [InlineData("read-error")]
    [InlineData("changed")]
    public async Task BadReadsCannotPublishOrOverwrite(string mode)
    {
        var reader = new FakeReader { Mode = mode }; string folder = TestMediaFixture.NewFolder();
        string target = Path.Combine(folder, "DISC-01.bin"); File.WriteAllText(target, "original");
        var result = await new DiscRipService(reader).RunAsync(Request(reader, folder), OverwritePolicy.Replace);
        Assert.Equal(1, result.Failed);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
    }
    [Theory]
    [InlineData(OverwritePolicy.Fail, ConversionStatus.Failed)]
    [InlineData(OverwritePolicy.Skip, ConversionStatus.Skipped)]
    [InlineData(OverwritePolicy.Replace, ConversionStatus.Succeeded)]
    public async Task OverwritePolicyApplies(OverwritePolicy policy, ConversionStatus expected)
    {
        var reader = new FakeReader(); string folder = TestMediaFixture.NewFolder();
        string target = Path.Combine(folder, "DISC-01.bin"); File.WriteAllText(target, "original");
        var result = await new DiscRipService(reader).RunAsync(Request(reader, folder), policy);
        Assert.Equal(expected, Assert.Single(result.Files).Status);
        if (policy != OverwritePolicy.Replace) Assert.Equal("original", File.ReadAllText(target));
    }
    [Fact]
    public async Task CancellationDiscardsTrackAndDoesNotReadNext()
    {
        using var cancel = new CancellationTokenSource();
        var reader = new FakeReader { Mode = "cancel", Cancel = cancel }; string folder = TestMediaFixture.NewFolder();
        var result = await new DiscRipService(reader).RunAsync(Request(reader, folder) with { Tracks = [1, 2] }, OverwritePolicy.Fail, token: cancel.Token);
        Assert.True(result.Cancelled); Assert.Equal(new[] { 1 }, reader.ReadTracks); Assert.Empty(Directory.GetFiles(folder));
    }
    [Fact]
    public async Task AskCanSkipAndCancelWithoutReplacing()
    {
        foreach (var decision in new[] { ConflictDecision.Skip, ConflictDecision.Cancel })
        {
            var reader = new FakeReader(); string folder = TestMediaFixture.NewFolder();
            File.WriteAllText(Path.Combine(folder, "DISC-01.bin"), "original");
            int calls = 0;
            var result = await new DiscRipService(reader).RunAsync(Request(reader, folder), OverwritePolicy.Ask, resolve: (conflict, _) => { calls++; Assert.Single(conflict.ExistingFiles); return Task.FromResult(decision); });
            Assert.Equal(1, calls); Assert.Equal("original", File.ReadAllText(Path.Combine(folder, "DISC-01.bin")));
            Assert.Equal(decision == ConflictDecision.Cancel, result.Cancelled);
        }
    }
    [Fact]
    public async Task InvalidRequestsNeverReadHardware()
    {
        var reader = new FakeReader(); var request = Request(reader, TestMediaFixture.NewFolder());
        var invalid = new[] { request with { Options = new() }, request with { Tracks = [] }, request with { Tracks = [3] }, request with { Tracks = [1, 1] }, request with { DiscName = "../escape" }, request with { Disc = reader.Disc with { SupportsCdg = false } } };
        foreach (var bad in invalid) await Assert.ThrowsAnyAsync<Exception>(() => new DiscRipService(reader).RunAsync(bad, OverwritePolicy.Fail));
        Assert.Empty(reader.ReadTracks);
    }
    [Theory]
    [InlineData("no-drive", "No optical drives")]
    [InlineData("scan-error", "Reader unavailable")]
    public async Task MissingDrivesAndReaderFailuresReturnToEditableState(string mode, string message)
    {
        var model = new DiscRipViewModel(Options, OverwritePolicy.Fail, new FakeReader { Mode = mode });
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.IsBusy); Assert.False(model.CanStart); Assert.Contains(message, model.Status);
    }
    [Fact]
    public async Task ViewModelHonorsTrackSelectionAndDisablesCompletedStart()
    {
        var reader = new FakeReader();
        var model = new DiscRipViewModel(Options, OverwritePolicy.Fail, reader) { Destination = TestMediaFixture.NewFolder() };
        await model.RefreshCommand.ExecuteAsync(null);
        model.Tracks[0].Selected = false;
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 2 }, reader.ReadTracks); Assert.Equal(1, model.Result!.Succeeded); Assert.False(model.CanStart);
    }
    [Fact]
    public void OptInPersistsAndIsHiddenByDefault()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "options.xmls");
        var store = new SettingsStore(path);
        Assert.False(store.Load().ExperimentalDiscRipping);
        var main = new MainViewModel(path);
        Assert.DoesNotContain(main.VisibleSections.SelectMany(section => section.Operations), operation => operation.IsRip);
        Assert.False(main.RunDiscRipCommand.CanExecute(null));
        var draft = new SettingsViewModel(main.Options) { ExperimentalDiscRipping = true };
        Assert.True(draft.HasChanges);
        main.SaveOptions(draft.ToOptions());
        Assert.True(store.Load().ExperimentalDiscRipping);
        Assert.True(main.RunDiscRipCommand.CanExecute(null));
        Assert.DoesNotContain(main.VisibleSections, section => section.Section == OperationSection.DiscRipping);
        Assert.DoesNotContain(main.VisibleSections.SelectMany(section => section.Operations), operation => operation.IsRip);
        main.SaveOptions(main.Options with { ExperimentalDiscRipping = false });
        Assert.DoesNotContain(main.VisibleSections.SelectMany(section => section.Operations), operation => operation.IsRip);
        Assert.False(main.RunDiscRipCommand.CanExecute(null));
    }
}
