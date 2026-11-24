using System.Globalization;
using System.IO.Compression;
using System.Text;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Tests.Benchmarks;

namespace KaraokeToolkit.Tests.Conversion;

public class KmaConversionTests
{
    [Theory]
    [InlineData(1, ".wma")]
    [InlineData(0, ".wma")]
    [InlineData(3, ".wma")]
    [InlineData(4, ".wma")]
    [InlineData(5, ".mp3")]
    [InlineData(6, ".mp3")]
    [InlineData(6, ".wma")]
    [InlineData(6, ".wav")]
    public async Task ExtractsSupportedKaraokePayloadAndKeepsOriginal(int type, string extension)
    {
        string folder = TestMediaFixture.NewFolder();
        string destination = TestMediaFixture.NewFolder();
        var (source, audio, graphics) = await CreateKmaAsync(folder, type, extension);
        byte[] original = File.ReadAllBytes(source);
        var executor = new OperationExecutionService(new RecoveryStore(TestMediaFixture.NewFolder()));
        var result = await executor.ExecuteAsync(new(OperationId.KmaToMp3G, [source])
        {
            Options = new() { AudioEncoder = AudioEncoder.WMA, AudioBitrate = 192, DeleteOldFiles = true },
            OutputFolder = destination,
            ValidateBeforeConversion = true
        });

        var entry = Assert.Single(result.Report.Entries);
        Assert.True(entry.Status == "Succeeded", entry.Message);
        string mp3 = Path.Combine(destination, "song.mp3");
        string cdg = Path.ChangeExtension(mp3, ".cdg");
        Assert.Equal(new[] { mp3, cdg }.Order(), Directory.GetFiles(destination).Order());
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Equal(graphics, File.ReadAllBytes(cdg));
        if (extension == ".mp3")
            Assert.Equal(audio, File.ReadAllBytes(mp3));
        else
        {
            var info = await MediaProbe.AnalyseAsync(mp3, FFmpegRuntime.Options(destination));
            Assert.Equal("mp3", info.PrimaryAudioStream?.CodecName);
            Assert.Equal(192000, info.PrimaryAudioStream?.BitRate);
            Assert.InRange(info.Duration.TotalSeconds, 5.9, 6.1);
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(3)]
    public async Task DesktopRecognizesKmaAndReportsGraphicsWarnings(int graphicsSeconds)
    {
        string folder = TestMediaFixture.NewFolder();
        var (source, expectedAudio, expectedGraphics) = await CreateKmaAsync(folder, 5, ".mp3", graphicsSeconds: graphicsSeconds);
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "options.xmls"));
        model.SetInputs([source]);

        Assert.Equal(OperationId.KmaToMp3G, model.SelectedOperation?.Action);
        Assert.Equal(new[] { ".kma" }, model.SelectedOperation?.SourceFileExtensions);
        Assert.True(model.CanStart);
        await model.StartCommand.ExecuteAsync(null);

        var entry = Assert.Single(model.LastReport!.Entries);
        Assert.Equal("Succeeded", entry.Status);
        if (graphicsSeconds < 6)
        {
            Assert.Contains("Possible sync issue", entry.Message);
            Assert.Equal(RunStatus.Warning, model.StatusOutcome);
            Assert.Single(model.Activity, row => row.Message.Contains("Possible sync issue"));
        }
        else
        {
            Assert.DoesNotContain("Warning", entry.Message);
            Assert.Equal(RunStatus.Success, model.StatusOutcome);
        }
        Assert.Equal(expectedAudio, File.ReadAllBytes(Path.ChangeExtension(source, ".mp3")));
        Assert.Equal(expectedGraphics, File.ReadAllBytes(Path.ChangeExtension(source, ".cdg")));
    }

    [Theory]
    [InlineData(5, ".mp3")]
    [InlineData(6, ".mp3")]
    public async Task RemovesBoundedPaddingAndPreservesThePayload(int type, string extension)
    {
        string folder = TestMediaFixture.NewFolder();
        var (source, audio, graphics) = await CreateKmaAsync(folder, type, extension, padding: 64 * 1024);
        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder()))
            .RunAsync(new(OperationId.KmaToMp3G, folder, new()));
        var file = Assert.Single(result.Files);
        Assert.True(file.Status == ConversionStatus.Succeeded, file.Message);
        Assert.Null(file.Warning);
        Assert.Equal(audio, File.ReadAllBytes(Path.ChangeExtension(source, ".mp3")));
        Assert.Equal(graphics, File.ReadAllBytes(Path.ChangeExtension(source, ".cdg")));
    }

    [Fact]
    public async Task RejectsPaddingBeyondTheLimit()
    {
        string folder = TestMediaFixture.NewFolder();
        var (source, _, _) = await CreateKmaAsync(folder, 5, ".mp3", padding: 64 * 1024 + 1);
        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder()))
            .RunAsync(new(OperationId.KmaToMp3G, folder, new()));
        var file = Assert.Single(result.Files);
        Assert.Equal(ConversionStatus.Failed, file.Status);
        Assert.Contains("zero padding", file.Message);
        Assert.False(File.Exists(Path.ChangeExtension(source, ".mp3")));
    }

    [Theory]
    [InlineData(3, ".wma")]
    [InlineData(5, ".mp3")]
    public async Task IgnoresAlignedFalseSeparatorsInsideGraphics(int type, string extension)
    {
        string folder = TestMediaFixture.NewFolder();
        var (source, audio, graphics) = await CreateKmaAsync(folder, type, extension);
        byte[] bytes = File.ReadAllBytes(source);
        "FEEDFACE"u8.CopyTo(graphics.AsSpan(16));
        graphics.CopyTo(bytes, bytes.Length - graphics.Length);
        File.WriteAllBytes(source, bytes);

        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder()))
            .RunAsync(new(OperationId.KmaToMp3G, folder, new()));
        var file = Assert.Single(result.Files);
        Assert.True(file.Status == ConversionStatus.Succeeded, file.Message);
        Assert.Equal(graphics, File.ReadAllBytes(Path.ChangeExtension(source, ".cdg")));
        if (extension == ".mp3") Assert.Equal(audio, File.ReadAllBytes(Path.ChangeExtension(source, ".mp3")));
    }

    [Fact]
    public async Task RejectsASingleAlignedSeparatorInsideWma()
    {
        string folder = TestMediaFixture.NewFolder();
        var (source, audio, _) = await CreateKmaAsync(folder, 3, ".wma");
        byte[] bytes = File.ReadAllBytes(source);
        int actualSeparator = 256 + audio.Length;
        "FEEDFACE"u8.CopyTo(bytes.AsSpan(actualSeparator - 24));
        bytes[actualSeparator] = 0;
        File.WriteAllBytes(source, bytes);

        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder()))
            .RunAsync(new(OperationId.KmaToMp3G, folder, new()));
        var file = Assert.Single(result.Files);
        Assert.Equal(ConversionStatus.Failed, file.Status);
        Assert.Contains("separator lies inside the audio stream", file.Message);
        Assert.False(File.Exists(Path.ChangeExtension(source, ".mp3")));
    }

    [Fact]
    public async Task RejectsCompetingSeparatorsWhenTheAudioEndpointCannotBeVerified()
    {
        string folder = TestMediaFixture.NewFolder();
        var (source, audio, graphics) = await CreateKmaAsync(folder, 5, ".mp3");
        byte[] bytes = File.ReadAllBytes(source);
        int separator = 256 + audio.Length;
        "FEEDFACE"u8.CopyTo(bytes.AsSpan(bytes.Length - graphics.Length + 16));
        File.WriteAllBytes(source, [.. bytes.AsSpan(0, separator), .. new byte[24], .. bytes.AsSpan(separator)]);

        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder()))
            .RunAsync(new(OperationId.KmaToMp3G, folder, new()));
        var file = Assert.Single(result.Files);
        Assert.Equal(ConversionStatus.Failed, file.Status);
        Assert.Contains("no uniquely verified split", file.Message);
        Assert.False(File.Exists(Path.ChangeExtension(source, ".mp3")));
        Assert.False(File.Exists(Path.ChangeExtension(source, ".cdg")));
    }

    [Fact]
    public async Task InconsistentMpegFrameRatesCannotResolveCompetingSeparators()
    {
        // MPEG-1 Layer III at 128 kbps, first at 44.1 kHz and then at 32 kHz.
        byte[] first = new byte[417];
        byte[] second = new byte[576];
        new byte[] { 0xff, 0xfb, 0x90, 0 }.CopyTo(first, 0);
        new byte[] { 0xff, 0xfb, 0x98, 0 }.CopyTo(second, 0);
        byte[] graphics = new byte[48];
        "FEEDFACE"u8.CopyTo(graphics.AsSpan(16));
        using var input = new MemoryStream([.. first, .. second, .. "FEEDFACE"u8, .. graphics]);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            KmaPayloadChecks.FindSeparatorAsync(input, 0, ".mp3", CancellationToken.None));
        Assert.Contains("no uniquely verified split", error.Message);
    }

    [Fact]
    public async Task GraphicsWarningSurvivesOverwriteReview()
    {
        string folder = TestMediaFixture.NewFolder();
        var (source, _, graphics) = await CreateKmaAsync(folder, 5, ".mp3");
        byte[] bytes = File.ReadAllBytes(source);
        bytes[bytes.Length - graphics.Length + 1] = 63;
        File.WriteAllBytes(source, bytes);
        File.WriteAllBytes(Path.ChangeExtension(source, ".cdg"), [1, 2, 3]);
        var service = new SelectionConversionService(new RecoveryStore(TestMediaFixture.NewFolder()));
        var result = await service.RunAsync(new(OperationId.KmaToMp3G, [source], new(), OverwritePolicy.Ask),
            resolveConflicts: (conflicts, _) =>
            {
                Assert.Single(conflicts);
                return Task.FromResult<IReadOnlyList<ConflictDecision>?>([ConflictDecision.Replace]);
            });
        var file = Assert.Single(result.Files);
        Assert.Equal(ConversionStatus.Succeeded, file.Status);
        Assert.Contains("unrecognized graphics instructions", file.Warning);
        Assert.Equal(bytes.AsSpan(bytes.Length - graphics.Length).ToArray(), File.ReadAllBytes(Path.ChangeExtension(source, ".cdg")));
    }

    [Fact]
    public void PublicationWarningSurvivesReopeningTheCheckpoint()
    {
        string folder = TestMediaFixture.NewFolder();
        string warned = Path.Combine(folder, "ABC-01.kma");
        string clean = Path.Combine(folder, "ABC-02.kma");
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        string journal;
        using (var run = new RunSession(store, new(OperationId.KmaToMp3G, folder, new()), OverwritePolicy.Fail, [warned, clean]))
        {
            journal = run.Journal;
            run.Begin(warned);
            run.NotePublicationWarning("Possible sync issue.");
        }

        using var recovered = new RunSession(store, journal);
        recovered.Committed([Path.ChangeExtension(warned, ".mp3"), Path.ChangeExtension(warned, ".cdg")]);
        recovered.Begin(clean);
        recovered.Committed([Path.ChangeExtension(clean, ".mp3"), Path.ChangeExtension(clean, ".cdg")]);

        Assert.Equal("Possible sync issue.", recovered.Data.Results[warned].Warning);
        Assert.Null(recovered.Data.Results[clean].Warning);
        recovered.Finish();
    }

    [Fact]
    public async Task RejectsTruncatedAndAudioOnlyInputsAndContinues()
    {
        string folder = TestMediaFixture.NewFolder();
        var (source, _, graphics) = await CreateKmaAsync(folder, 5, ".mp3");
        byte[] bytes = File.ReadAllBytes(source);
        string truncated = Path.Combine(folder, "bad.kma");
        File.WriteAllBytes(truncated, bytes[..^1]);
        bytes[169] = 7;
        string audioOnly = Path.Combine(folder, "audio.kma");
        File.WriteAllBytes(audioOnly, bytes);

        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder()))
            .RunAsync(new(OperationId.KmaToMp3G, folder, new()));

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(2, result.Failed);
        Assert.Contains("Audio-only", result.Files.Single(file => file.Source == audioOnly).Message);
        Assert.Contains("separator", result.Files.Single(file => file.Source == truncated).Message);
        Assert.Equal(graphics, File.ReadAllBytes(Path.ChangeExtension(source, ".cdg")));
        Assert.False(File.Exists(Path.ChangeExtension(truncated, ".mp3")));
        Assert.False(File.Exists(Path.ChangeExtension(audioOnly, ".cdg")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task PlansBothOutputsAndSkipsTheWholePairOnConflict()
    {
        string folder = TestMediaFixture.NewFolder();
        var (source, _, _) = await CreateKmaAsync(folder, 5, ".mp3");
        string mp3 = Path.ChangeExtension(source, ".mp3");
        string cdg = Path.ChangeExtension(source, ".cdg");
        byte[] oldGraphics = [1, 2, 3];
        File.WriteAllBytes(cdg, oldGraphics);
        var request = new OperationRequest(OperationId.KmaToMp3G, [source]) { OverwritePolicy = OverwritePolicy.Skip };
        var plan = await new OperationPlanner().PlanAsync(request);
        Assert.Equal(new[] { mp3, cdg }, Assert.Single(plan.Report.Entries).Outputs);
        Assert.False(File.Exists(mp3));

        var result = await new OperationExecutionService(new RecoveryStore(TestMediaFixture.NewFolder())).ExecuteAsync(request);
        Assert.Equal("Skipped", Assert.Single(result.Report.Entries).Status);
        Assert.Equal(oldGraphics, File.ReadAllBytes(cdg));
        Assert.False(File.Exists(mp3));
    }

    private static async Task<(string Source, byte[] Audio, byte[] Graphics)> CreateKmaAsync(string folder, int type, string extension,
        int graphicsSeconds = 6, int padding = 0)
    {
        string media = TestMediaFixture.NewFolder();
        string wave = Path.Combine(media, "audio.wav");
        string encoded = Path.ChangeExtension(wave, extension);
        AudioBenchmarks.WriteWave(wave, 6);
        if (extension != ".wav")
            await AudioEncoderService.EncodeAsync(wave, encoded, extension == ".mp3" ? AudioEncoder.MP3 : AudioEncoder.WMA,
                128, CancellationToken.None);
        byte[] audio = File.ReadAllBytes(encoded);
        byte[] graphics = new byte[graphicsSeconds * 300 * 24];
        for (int i = 0; i < graphics.Length; i += 24)
        {
            graphics[i] = 9;
            graphics[i + 1] = 1;
            graphics[i + 4] = (byte)(i / 24 % 16);
        }

        byte[] header = new byte[256];
        header[165] = type == 0 ? (byte)0 : (byte)'@';
        header[169] = type == 0 ? (byte)0xff : (byte)type;
        Encoding.ASCII.GetBytes(audio.Length.ToString("D8", CultureInfo.InvariantCulture), header.AsSpan(240, 8));
        Encoding.ASCII.GetBytes(graphics.Length.ToString("D8", CultureInfo.InvariantCulture), header.AsSpan(248, 8));
        string source = Path.Combine(folder, "song.KMA");
        using var output = File.Create(source);
        output.Write(header);
        if (padding > 0) output.Write(new byte[padding]);
        if (type == 6)
        {
            using var payload = new MemoryStream();
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var track = zip.CreateEntry("track" + extension).Open()) track.Write(audio);
                using (var cdg = zip.CreateEntry("track.cdg").Open()) cdg.Write(graphics);
            }
            payload.Position = 0;
            payload.CopyTo(output);
        }
        else if (type is 0 or 1)
        {
            const int chunkSize = 31_824;
            for (int offset = 0; offset < Math.Max(audio.Length, graphics.Length); offset += chunkSize)
            {
                if (offset < graphics.Length) output.Write(graphics.AsSpan(offset, Math.Min(chunkSize, graphics.Length - offset)));
                if (offset < audio.Length) output.Write(audio.AsSpan(offset, Math.Min(chunkSize, audio.Length - offset)));
            }
        }
        else
        {
            output.Write(audio);
            output.Write("FEEDFACE"u8);
            output.Write(graphics);
        }
        return (source, audio, graphics);
    }
}
