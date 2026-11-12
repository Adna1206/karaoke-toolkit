using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Tests.Conversion;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Core.Tests.Audio;

public class AudioConversionContractTests
{
    private static ConversionOptions Options(AudioEncoder encoder, int bitrate = 128) => new()
    {
        AudioEncoder = encoder, AudioBitrate = bitrate, DeleteOldFiles = false,
        AutoCreateCueText = false, RenameBins = false, AutoRenameZip = false
    };

    [Theory]
    [InlineData(AudioEncoder.MP3, 128, ".mp3")]
    [InlineData(AudioEncoder.MP3, 192, ".mp3")]
    [InlineData(AudioEncoder.OGG, 128, ".ogg")]
    [InlineData(AudioEncoder.OGG, 192, ".ogg")]
    public async Task EncodedBinAudioRetainsDurationSampleAlignmentAndGraphics(AudioEncoder encoder, int bitrate, string extension)
    {
        string folder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        byte[] expectedPcm = File.ReadAllBytes(Path.Combine(folder, "song.wav"))[44..];
        byte[] expectedGraphics = File.ReadAllBytes(Path.Combine(folder, "song.cdg"));
        string input = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(input, Action.Bin_To_AudioG, "keep");
        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(new(Action.Bin_To_AudioG, input, Options(encoder, bitrate)));
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(f => f.Message)));
        Assert.Equal(expectedGraphics, File.ReadAllBytes(Path.Combine(input, "song.cdg")));
        string pcm = Path.Combine(input, "decoded.pcm");
        await AudioDecoderService.DecodeAsync(Path.Combine(input, "song" + extension), pcm, default);
        byte[] actual = File.ReadAllBytes(pcm);
        Assert.InRange(Math.Abs(actual.Length - expectedPcm.Length), 0, 16384);
        double dot = 0, actualEnergy = 0, expectedEnergy = 0;
        // Compare a fixed, early sample window. Correct duration alone cannot catch encoder delay.
        for (int i = 4000; i < 16000; i += 4)
        {
            double a = BitConverter.ToInt16(actual, i), b = BitConverter.ToInt16(expectedPcm, i);
            dot += a * b; actualEnergy += a * a; expectedEnergy += b * b;
        }
        Assert.True(dot / Math.Sqrt(actualEnergy * expectedEnergy) > .98, "Audio must remain aligned to the source timeline.");
    }

    [Theory]
    [InlineData(AudioEncoder.MP3, ".mp3")]
    [InlineData(AudioEncoder.OGG, ".ogg")]
    public async Task CompressedAudioToBinPreservesDecodedSamplesAndGraphics(AudioEncoder encoder, string extension)
    {
        string folder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        string wav = Path.Combine(folder, "song.wav"), audio = Path.Combine(folder, "song" + extension);
        await AudioEncoderService.EncodeAsync(wav, audio, encoder, 128, default);
        File.Delete(wav); // Exactly one matching audio input.
        string decoded = Path.Combine(TestMediaFixture.NewFolder(), "expected.pcm");
        await AudioDecoderService.DecodeAsync(audio, decoded, default);
        byte[] graphics = File.ReadAllBytes(Path.Combine(folder, "song.cdg"));
        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(new(Action.AudioG_To_Bin, folder, Options(encoder)));
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(f => f.Message)));
        ConversionContractTests.AssertBinPayload(File.ReadAllBytes(Path.Combine(folder, "song.bin")), File.ReadAllBytes(decoded), graphics);
        Assert.True(File.Exists(audio)); Assert.Equal(graphics, File.ReadAllBytes(Path.Combine(folder, "song.cdg")));
    }
}
