namespace KaraokeToolkit.Core.Audio;

internal static class KmaAudio
{
    internal static async Task<TimeSpan> PrepareMp3Async(string input, string mp3, int bitrate, CancellationToken token)
    {
        string folder = Path.GetDirectoryName(input)!;
        string pcm = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".pcm");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            var info = await MediaProbe.AnalyseAsync(input, FFmpegRuntime.Options(folder), stop.Token);
            string? codec = info.PrimaryAudioStream?.CodecName;
            bool expectedFormat = Path.GetExtension(input) switch
            {
                ".mp3" => info.Format == "mp3" && codec == "mp3",
                ".wma" => info.Format == "asf" && codec is "wmav1" or "wmav2" or "wmapro" or "wmalossless" or "wmavoice",
                ".wav" => info.Format == "wav" && codec?.StartsWith("pcm_", StringComparison.Ordinal) == true,
                _ => false
            };
            if (!expectedFormat)
                throw new InvalidDataException("KMA audio does not match its expected format.");

            // Decoding verifies the complete stream. Encoding from consecutive PCM
            // samples keeps the audio timeline independent of container timestamps.
            await AudioDecoderService.DecodeAsync(input, pcm, stop.Token, resampleForMp3G: true, allowMono: true);
            if (codec != "mp3")
                await AudioEncoderService.EncodePcmToMp3Async(pcm, mp3, bitrate, stop.Token);
            return TimeSpan.FromSeconds(new FileInfo(pcm).Length / (44100.0 * 4));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("KMA audio processing exceeded the 30-minute limit.");
        }
        finally { File.Delete(pcm); }
    }
}
