using FFMpegCore;
namespace KaraokeToolkit.Core.Audio;

/// <summary>Decodes one local 44.1 kHz stereo stream into signed 16-bit PCM without implicit resampling.</summary>
internal static class AudioDecoderService
{
    internal static async Task DecodeAsync(string input, string pcm, CancellationToken token, bool resampleForMp3G = false, bool allowMono = false)
    {
        token.ThrowIfCancellationRequested();
        input = FFmpegRuntime.LocalPath(input); pcm = FFmpegRuntime.LocalPath(pcm);
        if (File.Exists(pcm)) throw new IOException("PCM output already exists.");
        var options = FFmpegRuntime.Options(Path.GetDirectoryName(pcm)!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            var info = await MediaProbe.AnalyseAsync(input, options, stop.Token);
            var audio = info.PrimaryAudioStream ?? throw new InvalidDataException("Input contains no audio stream.");
            if (!resampleForMp3G && (audio.SampleRateHz != 44100 || audio.Channels != 2))
                throw new InvalidDataException("BIN audio must be 44.1 kHz stereo; resampling is not implemented.");
            if (resampleForMp3G && (audio.SampleRateHz <= 0 || (audio.Channels != 2 && !(allowMono && audio.Channels == 1))))
                throw new InvalidDataException(allowMono ? "MP3+G transcoding requires mono or stereo audio with a valid sample rate."
                    : "MP3+G transcoding requires stereo audio with a valid sample rate.");
            await FFMpegArguments.FromFileInput(input, false, o => o.WithCustomArgument("-protocol_whitelist file,pipe -err_detect explode"))
                .OutputToFile(pcm, false, o => o.WithCustomArgument("-map 0:a:0 -vn -sn -dn -c:a pcm_s16le " +
                    (resampleForMp3G ? "-ar 44100 -ac 2 " : "") + "-f s16le -xerror"))
                .CancellableThrough(stop.Token).ProcessAsynchronously(true, options);
            stop.Token.ThrowIfCancellationRequested();
            long length = new FileInfo(pcm).Length;
            if (length <= 0 || length % 4 != 0 || length > uint.MaxValue)
                throw new InvalidDataException("Audio did not decode into complete stereo samples within the 4 GiB limit.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Audio decoding exceeded the 30-minute limit."); }
    }
}
