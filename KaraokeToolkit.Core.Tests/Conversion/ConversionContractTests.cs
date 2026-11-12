using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using System.IO.Compression;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Core.Tests.Conversion;

/// <summary>Expected file-format and workflow outcomes, independent of any reference executable.</summary>
public class ConversionContractTests
{
    private static ConversionOptions Options => new()
    {
        DeleteOldFiles = false, RenameBins = false, AutoRenameZip = false, AutoCreateCueText = false
    };
    private static ConversionService Service() => new(new RecoveryStore(TestMediaFixture.NewFolder()));
    private static void Succeeded(ConversionResult result, int count = 1)
    {
        Assert.Equal(0, result.Failed);
        Assert.Equal(count, result.Succeeded);
        Assert.False(result.Cancelled);
    }

    internal static void AssertBinPayload(byte[] actual, byte[] pcm, byte[] graphics)
    {
        // CD+G BIN contract: 2352 PCM bytes followed by 96 CDG symbols per 2448-byte sector.
        int sectors = Math.Max((pcm.Length + 2351) / 2352, (graphics.Length + 95) / 96);
        Assert.Equal(sectors * 2448, actual.Length);
        for (int sector = 0; sector < sectors; sector++)
        {
            byte[] expectedAudio = new byte[2352], expectedGraphics = new byte[96];
            int audioStart = sector * 2352, graphicsStart = sector * 96;
            if (audioStart < pcm.Length) Array.Copy(pcm, audioStart, expectedAudio, 0, Math.Min(2352, pcm.Length - audioStart));
            if (graphicsStart < graphics.Length) Array.Copy(graphics, graphicsStart, expectedGraphics, 0, Math.Min(96, graphics.Length - graphicsStart));
            Assert.Equal(expectedAudio, actual.AsSpan(sector * 2448, 2352).ToArray());
            Assert.Equal(expectedGraphics, actual.AsSpan(sector * 2448 + 2352, 96).ToArray());
        }
    }

    [Fact]
    public async Task WaveAndGraphicsProduceExpectedInterleavedBin()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        byte[] wave = File.ReadAllBytes(Path.Combine(folder, "song.wav"));
        byte[] cdg = File.ReadAllBytes(Path.Combine(folder, "song.cdg"));
        Succeeded(await Service().RunAsync(new(Action.AudioG_To_Bin, folder, Options)));
        AssertBinPayload(File.ReadAllBytes(Path.Combine(folder, "song.bin")), wave[44..], cdg);
        Assert.Equal(wave, File.ReadAllBytes(Path.Combine(folder, "song.wav")));
        Assert.Equal(cdg, File.ReadAllBytes(Path.Combine(folder, "song.cdg")));
    }

    [Theory]
    [InlineData(AudioEncoder.MP3, ".mp3")]
    [InlineData(AudioEncoder.OGG, ".ogg")]
    public async Task BinEncodesReadableAudioAndPreservesEveryGraphicsByte(AudioEncoder encoder, string extension)
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.Bin_To_AudioG, "keep");
        byte[] bin = File.ReadAllBytes(Path.Combine(folder, "song.bin"));
        Succeeded(await Service().RunAsync(new(Action.Bin_To_AudioG, folder, Options with { AudioEncoder = encoder })));
        byte[] cdg = File.ReadAllBytes(Path.Combine(folder, "song.cdg"));
        for (int sector = 0; sector < 150; sector++)
            Assert.Equal(bin.AsSpan(sector * 2448 + 2352, 96).ToArray(), cdg.AsSpan(sector * 96, 96).ToArray());
        string pcm = Path.Combine(folder, "verified.pcm");
        await AudioDecoderService.DecodeAsync(Path.Combine(folder, "song" + extension), pcm, default);
        Assert.InRange(Math.Abs(new FileInfo(pcm).Length - 150 * 2352), 0, 16384);
        Assert.Equal(bin, File.ReadAllBytes(Path.Combine(folder, "song.bin")));
    }

    [Fact]
    public async Task ZipToBinAndBinToZipPreservePayloadsAndMemberNames()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        byte[] wave = File.ReadAllBytes(Path.Combine(folder, "song.wav")), cdg = File.ReadAllBytes(Path.Combine(folder, "song.cdg"));
        var service = Service();
        Succeeded(await service.RunAsync(new(Action.AudioG_To_Zip, folder, Options)));
        string extracted = TestMediaFixture.NewFolder();
        File.Copy(Path.Combine(folder, "song.zip"), Path.Combine(extracted, "song.zip"));
        Succeeded(await service.RunAsync(new(Action.Zip_To_Bin, extracted, Options)));
        AssertBinPayload(File.ReadAllBytes(Path.Combine(extracted, "song.bin")), wave[44..], cdg);
        string packaged = TestMediaFixture.NewFolder();
        File.Copy(Path.Combine(extracted, "song.bin"), Path.Combine(packaged, "song.bin"));
        Succeeded(await service.RunAsync(new(Action.Bin_To_Zip, packaged, Options with { AudioEncoder = AudioEncoder.WAV })));
        using var zip = ZipFile.OpenRead(Path.Combine(packaged, "song.zip"));
        Assert.Equal(["song.cdg", "song.wav"], zip.Entries.Select(e => e.FullName).Order());
        Assert.Equal(wave, Read(zip, "song.wav"));
        Assert.Equal(cdg, Read(zip, "song.cdg"));
    }

    private static byte[] Read(ZipArchive zip, string name)
    {
        using var input = zip.GetEntry(name)!.Open();
        using var result = new MemoryStream(); input.CopyTo(result); return result.ToArray();
    }

    public static IEnumerable<object[]> MatrixCases()
    {
        foreach (var action in new[] { Action.AudioG_To_Bin, Action.Zip_To_Bin, Action.Bin_To_AudioG, Action.Bin_To_Zip })
            foreach (string mode in new[] { "keep", "delete", "recursive", "nonrecursive", "overwrite", "cancel" })
                yield return [action, mode];
        foreach (string mode in new[] { "missing-audio", "missing-graphics", "invalid-audio", "short-graphics" })
            yield return [Action.AudioG_To_Bin, mode];
        yield return [Action.Bin_To_AudioG, "truncated-bin"];
    }

    [Theory, MemberData(nameof(MatrixCases))]
    public async Task ConversionMatrixHonoursFileSafetyAndScope(Action action, string mode)
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, action, mode);
        if (mode == "overwrite" && action == Action.Bin_To_Zip)
        {
            using var existing = ZipFile.Open(Path.Combine(folder, "song.zip"), ZipArchiveMode.Create);
            using var writer = new StreamWriter(existing.CreateEntry("keep.txt").Open()); writer.Write("existing output");
        }
        if (mode is "recursive" or "nonrecursive") TestMediaFixture.Seed(Path.Combine(folder, "child"), action, "keep");
        var before = TestMediaFixture.Snapshot(folder);
        string[] sources = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        using var cancel = new CancellationTokenSource();
        var progress = new SynchronousProgress<ConversionProgress>(p => { if (mode == "cancel" && p.Message.StartsWith("Processing:")) cancel.Cancel(); });
        var result = await Service().RunAsync(new(action, folder, Options with
        {
            DeleteOldFiles = mode == "delete", RecurseFolders = mode != "nonrecursive", AudioEncoder = AudioEncoder.WAV
        }), progress: progress, cancellationToken: cancel.Token);
        if (mode == "cancel")
        {
            Assert.True(result.Cancelled); Assert.Equal(0, result.Succeeded);
            Assert.Equal(before, TestMediaFixture.Snapshot(folder)); return;
        }
        if (mode is "overwrite" or "missing-audio" or "invalid-audio" or "short-graphics" or "truncated-bin")
        {
            Assert.Equal(1, result.Failed); Assert.Equal(0, result.Succeeded);
            Assert.Equal(before, TestMediaFixture.Snapshot(folder)); return;
        }
        if (mode == "missing-graphics")
        {
            Assert.Equal(0, result.Found); Assert.Empty(result.Files);
            Assert.Equal(before, TestMediaFixture.Snapshot(folder)); return;
        }
        int expected = mode == "recursive" ? 2 : 1;
        Succeeded(result, expected); Assert.Equal(expected, result.Found);
        foreach (string source in sources)
            Assert.Equal(mode != "delete", File.Exists(source));
        if (mode == "nonrecursive") Assert.Empty(Directory.GetFiles(Path.Combine(folder, "child"), action == Action.Bin_To_AudioG ? "*.wav" : action == Action.Bin_To_Zip ? "*.zip" : "*.bin"));
        foreach (var file in result.Files)
        {
            Assert.All(file.Outputs, path => Assert.True(new FileInfo(path).Length > 0));
            if (action is Action.AudioG_To_Bin or Action.Zip_To_Bin)
            {
                string expectedFolder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(expectedFolder, Action.AudioG_To_Bin, "keep");
                AssertBinPayload(File.ReadAllBytes(Assert.Single(file.Outputs)), File.ReadAllBytes(Path.Combine(expectedFolder, "song.wav"))[44..], File.ReadAllBytes(Path.Combine(expectedFolder, "song.cdg")));
            }
            else
            {
                byte[] wave, cdg;
                if (action == Action.Bin_To_Zip)
                {
                    using var zip = ZipFile.OpenRead(Assert.Single(file.Outputs)); wave = Read(zip, "song.wav"); cdg = Read(zip, "song.cdg");
                    Assert.Equal(2, zip.Entries.Count);
                }
                else { wave = File.ReadAllBytes(file.Outputs.Single(p => p.EndsWith(".wav"))); cdg = File.ReadAllBytes(file.Outputs.Single(p => p.EndsWith(".cdg"))); }
                string expectedFolder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(expectedFolder, Action.Bin_To_AudioG, "keep");
                AssertBinPayload(File.ReadAllBytes(Path.Combine(expectedFolder, "song.bin")), wave[44..], cdg);
            }
        }
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(Action.AudioG_To_Bin)] [InlineData(Action.AudioG_To_Zip)]
    [InlineData(Action.Bin_To_AudioG)] [InlineData(Action.Bin_To_Zip)]
    [InlineData(Action.Zip_To_AudioG)] [InlineData(Action.Zip_To_Bin)]
    [InlineData(Action.Cleanup_Filename)] [InlineData(Action.Cleanup_Filenames)]
    [InlineData(Action.Create_Cue_And_Text_Files)]
    public async Task EmptyFoldersCompleteWithoutCreatingMedia(Action action)
    {
        string folder = TestMediaFixture.NewFolder();
        var result = await Service().RunAsync(new(action, folder, Options)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(result.Cancelled); Assert.Equal(0, result.Failed); Assert.Equal(0, result.Succeeded);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotTouchFiles()
    {
        string folder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        var before = TestMediaFixture.Snapshot(folder);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().RunAsync(new(Action.AudioG_To_Bin, folder, Options), cancellationToken: new(true)));
        Assert.Equal(before, TestMediaFixture.Snapshot(folder));
    }

    [Fact]
    public async Task MissingFolderIsRejectedBeforeConversion()
    {
        string folder = Path.Combine(TestMediaFixture.NewFolder(), "absent");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Service().RunAsync(new(Action.AudioG_To_Bin, folder, Options)));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task CallbackFailurePropagatesWithoutPublishingOrHanging()
    {
        string folder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        var before = TestMediaFixture.Snapshot(folder);
        var recovery = new RecoveryStore(TestMediaFixture.NewFolder());
        var error = await Assert.ThrowsAsync<ApplicationException>(() => new ConversionService(recovery).RunAsync(new(Action.AudioG_To_Bin, folder, Options),
            progress: new SynchronousProgress<ConversionProgress>(_ => throw new ApplicationException("test callback failure"))).WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Equal("test callback failure", error.Message);
        // A callback failure leaves a recoverable journal, but must not publish or modify media.
        Assert.Equal(before, TestMediaFixture.Snapshot(folder).Where(pair => !pair.Key.StartsWith(".karaoke-job-")));
        Assert.False(File.Exists(Path.Combine(folder, "song.bin")));
        Assert.Single(recovery.Find(folder, false));
    }
}
