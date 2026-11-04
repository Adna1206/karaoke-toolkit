using KaraokeToolkit.Core.Audio;
using FFMpegCore;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Tests.Benchmarks;
namespace KaraokeToolkit.Tests.Audio;

public class FFmpegAudioTests
{
    [Theory]
    [InlineData("wav")]
    [InlineData("flac")]
    public async Task LosslessDecodingPreservesEverySample(string format)
    {
        string folder = TestMediaFixture.NewFolder(), wav = Path.Combine(folder, "André song.wav");
        AudioBenchmarks.WriteWave(wav, 2);
        string input = wav;
        if (format == "flac")
        {
            input = Path.ChangeExtension(wav, ".flac");
            await FFMpegArguments.FromFileInput(wav).OutputToFile(input, false, o => o.WithAudioCodec("flac")).ProcessAsynchronously(true, FFmpegRuntime.Options(folder));
        }
        string pcm = Path.Combine(folder, "output.pcm");
        await AudioDecoderService.DecodeAsync(input, pcm, default);
        Assert.Equal(File.ReadAllBytes(wav)[44..], File.ReadAllBytes(pcm));
    }
    [Theory]
    [InlineData(AudioEncoder.MP3)]
    [InlineData(AudioEncoder.OGG)]
    [InlineData(AudioEncoder.WMA)]
    [InlineData(AudioEncoder.AAC)]
    public async Task LossyRoundTripKeepsSampleTimeline(AudioEncoder encoder)
    {
        string folder = TestMediaFixture.NewFolder(), wav = Path.Combine(folder, "source.wav");
        AudioBenchmarks.WriteWave(wav, 2);
        string encoded = Path.Combine(folder, "encoded" + AudioEncoderService.Extension(encoder));
        await AudioEncoderService.EncodeAsync(wav, encoded, encoder, 192, default);
        string pcm = Path.Combine(folder, "check.pcm");
        await AudioDecoderService.DecodeAsync(encoded, pcm, default);
        Assert.InRange(Math.Abs(new FileInfo(pcm).Length - (new FileInfo(wav).Length - 44)), 0, 4096);
        // Correlate near the beginning: acceptable sample values can differ, but a whole MP3 encoder-delay shift cannot pass.
        byte[] actual = File.ReadAllBytes(pcm), expected = File.ReadAllBytes(wav)[44..];
        double dot = 0, a2 = 0, b2 = 0;
        for (int i = 4000; i < 16000; i += 4)
        {
            double a = BitConverter.ToInt16(actual, i), b = BitConverter.ToInt16(expected, i);
            dot += a * b; a2 += a * a; b2 += b * b;
        }
        Assert.True(dot / Math.Sqrt(a2 * b2) > .98, "Decoded audio must remain aligned with the CDG timeline.");
    }
    [Theory]
    [InlineData(AudioEncoder.WAV)]
    [InlineData(AudioEncoder.FLAC)]
    public async Task LosslessOutputPreservesEverySample(AudioEncoder encoder)
    {
        string folder = TestMediaFixture.NewFolder(), wav = Path.Combine(folder, "source.wav");
        AudioBenchmarks.WriteWave(wav, 2);
        string encoded = Path.Combine(folder, "encoded" + AudioEncoderService.Extension(encoder));
        await AudioEncoderService.EncodeAsync(wav, encoded, encoder, 128, default);
        string pcm = Path.Combine(folder, "decoded.pcm");
        await AudioDecoderService.DecodeAsync(encoded, pcm, default);
        Assert.Equal(File.ReadAllBytes(wav)[44..], File.ReadAllBytes(pcm));
    }
    [Theory]
    [InlineData(64)]
    [InlineData(96)]
    [InlineData(128)]
    [InlineData(160)]
    [InlineData(192)]
    [InlineData(224)]
    [InlineData(256)]
    [InlineData(320)]
    public async Task WmaBitratesRemainAligned(int bitrate)
    {
        string folder = TestMediaFixture.NewFolder(), wav = Path.Combine(folder, "source.wav");
        AudioBenchmarks.WriteWave(wav, 2);
        string encoded = Path.Combine(folder, "encoded.wma");
        await AudioEncoderService.EncodeAsync(wav, encoded, AudioEncoder.WMA, bitrate, default);
        string pcm = Path.Combine(folder, "decoded.pcm");
        await AudioDecoderService.DecodeAsync(encoded, pcm, default);
        byte[] actual = File.ReadAllBytes(pcm), expected = File.ReadAllBytes(wav)[44..];
        double dot = 0, a2 = 0, b2 = 0;
        for (int i = 4000; i < 16000; i += 4)
        {
            double a = BitConverter.ToInt16(actual, i), b = BitConverter.ToInt16(expected, i);
            dot += a * b; a2 += a * a; b2 += b * b;
        }
        Assert.True(dot / Math.Sqrt(a2 * b2) > .95, "WMA audio must stay aligned with CDG graphics.");
    }
    [Fact]
    public async Task InvalidAudioAndNonStereoInputsFail()
    {
        string folder = TestMediaFixture.NewFolder(), input = Path.Combine(folder, "bad.mp3");
        File.WriteAllText(input, "not audio");
        await Assert.ThrowsAnyAsync<Exception>(() => AudioDecoderService.DecodeAsync(input, Path.Combine(folder, "bad.pcm"), default));
        string wav = Path.Combine(folder, "stereo.wav"), mono = Path.Combine(folder, "mono.wav");
        AudioBenchmarks.WriteWave(wav, 2);
        await FFMpegArguments.FromFileInput(wav).OutputToFile(mono, false, o => o.WithCustomArgument("-ac 1")).ProcessAsynchronously(true, FFmpegRuntime.Options(folder));
        await Assert.ThrowsAsync<InvalidDataException>(() => AudioDecoderService.DecodeAsync(mono, Path.Combine(folder, "mono.pcm"), default));
    }
    [Fact]
    public async Task CancellationAndExistingOutputsAreRespected()
    {
        string folder = TestMediaFixture.NewFolder(), wav = Path.Combine(folder, "source.wav"), pcm = Path.Combine(folder, "out.pcm");
        AudioBenchmarks.WriteWave(wav, 2);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AudioDecoderService.DecodeAsync(wav, pcm, new CancellationToken(true)));
        Assert.False(File.Exists(pcm));
        File.WriteAllText(pcm, "existing");
        await Assert.ThrowsAsync<IOException>(() => AudioDecoderService.DecodeAsync(wav, pcm, default));
        Assert.Equal("existing", File.ReadAllText(pcm));
    }
}
