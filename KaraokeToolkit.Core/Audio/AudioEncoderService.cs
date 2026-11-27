using FFMpegCore;
using System.Globalization;
namespace KaraokeToolkit.Core.Audio;

/// <summary>Encodes audio through FFmpeg and fully decodes the result to check its duration.</summary>
internal static class AudioEncoderService
{
    internal static readonly int[] SupportedBitrates = [64, 96, 128, 160, 192, 224, 256, 320];
    internal static bool UsesBitrate(AudioEncoder encoder) => encoder is AudioEncoder.MP3 or AudioEncoder.OGG or AudioEncoder.WMA or AudioEncoder.AAC;
    internal static string Extension(AudioEncoder encoder) => encoder switch
    {
        AudioEncoder.MP3 => ".mp3",
        AudioEncoder.OGG => ".ogg",
        AudioEncoder.WMA => ".wma",
        AudioEncoder.WAV => ".wav",
        AudioEncoder.FLAC => ".flac",
        AudioEncoder.AAC => ".m4a",
        _ => throw new ArgumentOutOfRangeException(nameof(encoder))
    };
    internal static void Validate(AudioEncoder encoder, int bitrate)
    {
        _ = Extension(encoder);
        if (UsesBitrate(encoder) && !SupportedBitrates.Contains(bitrate))
            throw new ArgumentOutOfRangeException(nameof(bitrate));
    }
    internal static Task EncodeAsync(string wav, string output, AudioEncoder encoder, int bitrate, CancellationToken token) =>
        EncodeAsync(wav, output, encoder, bitrate, token, rawPcm: false);

    internal static Task EncodePcmToMp3Async(string pcm, string output, int bitrate, CancellationToken token) =>
        EncodeAsync(pcm, output, AudioEncoder.MP3, bitrate, token, rawPcm: true);

    private static async Task EncodeAsync(string input, string output, AudioEncoder encoder, int bitrate, CancellationToken token, bool rawPcm)
    {
        token.ThrowIfCancellationRequested();
        Validate(encoder, bitrate);
        input = FFmpegRuntime.LocalPath(input); output = FFmpegRuntime.LocalPath(output);
        if (File.Exists(output)) throw new IOException("Encoded output already exists.");
        var options = FFmpegRuntime.Options(Path.GetDirectoryName(output)!);
        (string codec, string format) = encoder switch
        {
            AudioEncoder.MP3 => ("libmp3lame", "mp3"),
            AudioEncoder.OGG => ("libvorbis", "ogg"),
            AudioEncoder.WMA => ("wmav2", "asf"),
            AudioEncoder.WAV => ("pcm_s16le", "wav"),
            AudioEncoder.FLAC => ("flac", "flac"),
            AudioEncoder.AAC => ("aac", "ipod"),
            _ => throw new ArgumentOutOfRangeException(nameof(encoder))
        };
        string rate = UsesBitrate(encoder) ? $" -b:a {bitrate.ToString(CultureInfo.InvariantCulture)}k" : "";
        // FFmpeg's WMA 2/ASF round-trip trims 544 leading PCM frames. Prepending that
        // silence keeps the first audible sample aligned with the CDG timeline.
        string alignment = encoder == AudioEncoder.WMA ? " -af adelay=544S:all=1" : "";
        string inputFormat = rawPcm ? " -f s16le -ar 44100 -ac 2" : "";
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            await FFMpegArguments.FromFileInput(input, false, o => o.WithCustomArgument("-protocol_whitelist file,pipe -err_detect explode" + inputFormat))
                .OutputToFile(output, false, o => o.WithCustomArgument($"-map 0:a:0 -vn -sn -dn{alignment} -c:a {codec}{rate} -f {format} -xerror"))
                .CancellableThrough(stop.Token, TimeSpan.Zero).ProcessAsynchronously(ffOptions: options);
            stop.Token.ThrowIfCancellationRequested();
            if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new InvalidDataException("Encoder produced no audio.");
            string decoded = Path.Combine(Path.GetDirectoryName(output)!, Guid.NewGuid().ToString("N") + ".pcm");
            try
            {
                await AudioDecoderService.DecodeAsync(output, decoded, stop.Token);
                // BIN splitting produces a canonical 44-byte PCM WAV header.
                long expected = new FileInfo(input).Length - (rawPcm ? 0 : 44);
                if (Math.Abs(new FileInfo(decoded).Length - expected) > 16384) throw new InvalidDataException("Encoded audio duration differs from the source.");
            }
            finally { File.Delete(decoded); }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Audio encoding exceeded the 30-minute limit."); }
    }
}
