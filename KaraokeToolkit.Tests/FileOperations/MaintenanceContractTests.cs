using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Tests.FileOperations;

public class MaintenanceContractTests
{
    private static ConversionService Service() => new(new RecoveryStore(TestMediaFixture.NewFolder()));

    [Theory]
    [InlineData(Action.Cleanup_Filename)]
    [InlineData(Action.Cleanup_Filenames)]
    public async Task CleanupUsesSelectedFolderAndPreservesPairedPayloads(Action action)
    {
        string folder = TestMediaFixture.NewFolder();
        File.WriteAllBytes(Path.Combine(folder, "DISC-01_some_song.mp3"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(folder, "DISC-01_some_song.cdg"), new byte[96]);
        var result = await Service().RunAsync(new(action, folder, new() { DeleteOldFiles = false, AutoCreateCueText = false }));
        Assert.Equal(1, result.Succeeded); Assert.Equal(0, result.Failed);
        Assert.Equal(["DISC-01 Some Song.cdg", "DISC-01 Some Song.mp3"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(folder, "DISC-01 Some Song.mp3")));
        Assert.Equal(new byte[96], File.ReadAllBytes(Path.Combine(folder, "DISC-01 Some Song.cdg")));
    }

    [Fact]
    public async Task StandaloneCueActionCreatesCueAndTrackListingWithoutChangingBin()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.Bin_To_AudioG, "keep");
        byte[] bin = File.ReadAllBytes(Path.Combine(folder, "song.bin"));
        var result = await Service().RunAsync(new(Action.Create_Cue_And_Text_Files, folder, new()));
        Assert.Equal(1, result.Succeeded); Assert.Equal(0, result.Failed);
        Assert.Equal(bin, File.ReadAllBytes(Path.Combine(folder, "song.bin")));
        string cue = File.ReadAllText(Assert.Single(Directory.GetFiles(folder, "*.cue")));
        Assert.Contains("song.bin", cue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TRACK 01", cue);
        Assert.Contains("song", File.ReadAllText(Assert.Single(Directory.GetFiles(folder, "*.txt"))), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WmaOutputIsReadableAndPreservesBinAndGraphics()
    {
        string folder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(folder, Action.Bin_To_AudioG, "keep");
        byte[] bin = File.ReadAllBytes(Path.Combine(folder, "song.bin"));
        var result = await Service().RunAsync(new(Action.Bin_To_AudioG, folder,
            new() { AudioEncoder = AudioEncoder.WMA, RenameBins = false, DeleteOldFiles = false, AutoCreateCueText = false }));
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(f => f.Message)));
        string pcm = Path.Combine(folder, "decoded.pcm");
        await AudioDecoderService.DecodeAsync(Path.Combine(folder, "song.wma"), pcm, default);
        Assert.InRange(Math.Abs(new FileInfo(pcm).Length - 150 * 2352), 0, 16384);
        byte[] graphics = File.ReadAllBytes(Path.Combine(folder, "song.cdg"));
        Assert.Equal(150 * 96, graphics.Length);
        for (int sector = 0; sector < 150; sector++)
            Assert.Equal(bin.AsSpan(sector * 2448 + 2352, 96).ToArray(), graphics.AsSpan(sector * 96, 96).ToArray());
        Assert.Equal(bin, File.ReadAllBytes(Path.Combine(folder, "song.bin")));
    }
}
