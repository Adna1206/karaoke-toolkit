using FFMpegCore;
using System.Globalization;

namespace KaraokeToolkit.Core.Audio;

/// <summary>Transcodes local lossless stereo audio to a 44.1 kHz MP3 for an existing CDG pair.</summary>
internal static class LosslessAudioTranscoder
{
    internal static readonly string[] Extensions = [".wav", ".flac", ".ape", ".aif", ".aiff", ".m4a", ".wv"];

    internal static bool IsCandidate(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    internal static async Task EncodeAsync(string input, string output, int bitrate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsCandidate(input)) throw new NotSupportedException("Choose WAV, FLAC, APE, AIFF, ALAC (.m4a) or WavPack (.wv) audio.");
        if (!AudioEncoderService.SupportedBitrates.Contains(bitrate)) throw new ArgumentOutOfRangeException(nameof(bitrate));
        input = FFmpegRuntime.LocalPath(input);
        output = FFmpegRuntime.LocalPath(output);
        if (File.Exists(output)) throw new IOException("MP3 output already exists.");
        var options = FFmpegRuntime.Options(Path.GetDirectoryName(output)!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            var info = await MediaProbe.AnalyseAsync(input, options, stop.Token);
            var stream = info.PrimaryAudioStream ?? throw new InvalidDataException("Input contains no audio stream.");
            string codec = stream.CodecName.ToLowerInvariant();
            bool uncompressedPcm = codec.StartsWith("pcm_s", StringComparison.Ordinal) ||
                codec.StartsWith("pcm_u", StringComparison.Ordinal) ||
                codec.StartsWith("pcm_f", StringComparison.Ordinal);
            if (!(uncompressedPcm || codec is "flac" or "ape" or "alac" or "wavpack"))
                throw new NotSupportedException($"{stream.CodecName} is not a supported lossless audio codec.");
            if (stream.SampleRateHz <= 0 || stream.Channels != 2)
                throw new InvalidDataException("MP3+G transcoding requires stereo audio with a valid sample rate.");

            string sourcePcm = Path.Combine(Path.GetDirectoryName(output)!, Guid.NewGuid().ToString("N") + ".source.pcm");
            string encodedPcm = Path.Combine(Path.GetDirectoryName(output)!, Guid.NewGuid().ToString("N") + ".encoded.pcm");
            try
            {
                await AudioDecoderService.DecodeAsync(input, sourcePcm, stop.Token, resampleForMp3G: true);
                await FFMpegArguments.FromFileInput(input, false, o => o.WithCustomArgument("-protocol_whitelist file,pipe -err_detect explode"))
                    .OutputToFile(output, false, o => o.WithCustomArgument($"-map 0:a:0 -vn -sn -dn -c:a libmp3lame -ar 44100 -ac 2 -b:a {bitrate.ToString(CultureInfo.InvariantCulture)}k -f mp3 -xerror"))
                    .CancellableThrough(stop.Token, TimeSpan.Zero).ProcessAsynchronously(ffOptions: options);
                if (!File.Exists(output) || new FileInfo(output).Length == 0)
                    throw new InvalidDataException("Encoder produced no MP3 audio.");
                await AudioDecoderService.DecodeAsync(output, encodedPcm, stop.Token);
                if (Math.Abs(new FileInfo(sourcePcm).Length - new FileInfo(encodedPcm).Length) > 16384)
                    throw new InvalidDataException("Encoded audio duration differs from the source.");
            }
            finally
            {
                File.Delete(sourcePcm);
                File.Delete(encodedPcm);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("MP3+G transcoding exceeded the 30-minute limit.");
        }
    }
}
