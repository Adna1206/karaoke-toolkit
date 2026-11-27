using FFMpegCore;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KaraokeToolkit.Core.Audio;

internal sealed record AudioGainMeasurement(double? Lufs, double? PeakDbtp, long DurationUs, long Samples = 0);

internal static class AudioGainMedia
{
    internal static async Task<AudioGainMeasurement> MeasureAsync(string path, CancellationToken token)
    {
        var (output, log) = await RunAsync(path, null,
            "-af asetpts=N/SR/TB,astats=measure_perchannel=none:measure_overall=Number_of_samples,loudnorm=print_format=json -progress pipe:1", token);
        int start = log.LastIndexOf('{'), end = log.LastIndexOf('}');
        if (start < 0 || end < start) throw new InvalidDataException("FFmpeg returned no loudness measurement.");
        try
        {
            using var json = JsonDocument.Parse(log[start..(end + 1)]);
            double? Read(string name) => double.TryParse(json.RootElement.GetProperty(name).GetString(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : null;
            long duration = output.Split('\n').Where(line => line.StartsWith("out_time_us=", StringComparison.Ordinal))
                .Select(line => long.TryParse(line.AsSpan(12).Trim(), CultureInfo.InvariantCulture, out long value) ? value : 0)
                .LastOrDefault();
            if (duration <= 0) throw new InvalidDataException("Input contains no decodable audio.");
            const string sampleLabel = "Number of samples:";
            string? sampleLine = log.Split('\n').LastOrDefault(line => line.Contains(sampleLabel, StringComparison.Ordinal));
            ReadOnlySpan<char> sampleValue = sampleLine == null ? default :
                sampleLine.AsSpan(sampleLine.IndexOf(sampleLabel, StringComparison.Ordinal) + sampleLabel.Length).Trim();
            long.TryParse(sampleValue, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out long samples);
            return new(Read("input_i"), Read("input_tp"), duration, samples);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("FFmpeg returned invalid loudness measurements.", error);
        }
    }

    internal static async Task<(MediaStreamInfo Audio, string Encoder)> GetEncodingAsync(string input, CancellationToken token)
    {
        var info = await ProbeAsync(input, token);
        var audio = info.PrimaryAudioStream ?? throw new InvalidDataException("Input contains no audio stream.");
        if (audio.SampleRateHz <= 0 || audio.Channels <= 0)
            throw new InvalidDataException("Input has an invalid sample rate or channel count.");
        string extension = Path.GetExtension(input).ToLowerInvariant();
        string encoder = extension switch
        {
            ".mp3" when audio.CodecName == "mp3" => "libmp3lame",
            ".aac" or ".m4a" when audio.CodecName == "aac" => "aac",
            ".m4a" when audio.CodecName == "alac" => "alac",
            ".flac" when audio.CodecName == "flac" => "flac",
            ".ogg" when audio.CodecName == "vorbis" => "libvorbis",
            ".wma" when audio.CodecName is "wmav1" or "wmav2" => audio.CodecName,
            ".wav" when audio.CodecName is "pcm_u8" or "pcm_s16le" or "pcm_s24le" or "pcm_s32le"
                or "pcm_f32le" or "pcm_f64le" => audio.CodecName,
            _ => throw new NotSupportedException($"The {audio.CodecName} codec is unsupported for {extension} gain output.")
        };
        return (audio, encoder);
    }

    internal static async Task EncodeAsync(string input, string output, double gain, CancellationToken token,
        int? semitones = null, long sourceSamples = 0)
    {
        var (audio, encoder) = await GetEncodingAsync(input, token);
        string extension = Path.GetExtension(input).ToLowerInvariant();
        string filter = FormattableString.Invariant($"volume={gain:0.########}dB:precision=double");
        if (semitones is int shift)
        {
            int rate = audio.SampleRateHz;
            int shiftedRate = (int)Math.Round(rate * Math.Pow(2, shift / 12.0));
            double inverseTempo = (double)rate / shiftedRate;
            // Reset the timeline after restoring tempo. Trim or pad the tail to the decoded source length.
            filter = FormattableString.Invariant(
                $"aformat=sample_fmts=dbl,asetrate={shiftedRate},aresample={rate},atempo={inverseTempo:0.##########},apad=whole_len={sourceSamples},atrim=end_sample={sourceSamples},asetpts=N/SR/TB");
        }
        var arguments = new List<string>
        {
            "-af", filter, "-c:a", encoder,
            "-ar", audio.SampleRateHz.ToString(CultureInfo.InvariantCulture), "-ac", audio.Channels.ToString(CultureInfo.InvariantCulture),
            "-map_metadata", "0"
        };
        if (extension is ".mp3" or ".m4a" or ".flac")
            arguments.AddRange(["-map", "0:v:disp:attached_pic?", "-c:v", "copy", "-disposition:v", "attached_pic"]);
        if (encoder is "libmp3lame" or "aac" or "libvorbis" or "wmav1" or "wmav2")
        {
            // Use a consistent quality setting where the source bitrate is unknown.
            long bitrate = audio.BitRate > 0 ? audio.BitRate : 96000L * audio.Channels;
            arguments.AddRange(["-b:a", bitrate.ToString(CultureInfo.InvariantCulture)]);
        }
        if (encoder == "flac")
            arguments.AddRange(["-sample_fmt", "s32"]);
        await RunAsync(input, output, string.Join(' ', arguments), token);
        var encoded = await ProbeAsync(output, token);
        if (encoded.PrimaryAudioStream is not { } actual || actual.SampleRateHz != audio.SampleRateHz ||
            actual.Channels != audio.Channels || actual.CodecName != audio.CodecName)
            throw new InvalidDataException("Encoded audio changed codec, sample rate or channel count unexpectedly.");
    }

    private static async Task<MediaInfo> ProbeAsync(string path, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            return await MediaProbe.AnalyseAsync(path, FFmpegRuntime.Options(Path.GetDirectoryName(path)!), timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Audio probing exceeded 30 minutes.");
        }
    }

    private static async Task<(string Output, string Log)> RunAsync(string path, string? output, string arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        path = FFmpegRuntime.LocalPath(path);
        if (output != null) output = FFmpegRuntime.LocalPath(output);
        var options = FFmpegRuntime.Options(Path.GetDirectoryName(path)!);
        var stdout = new StringBuilder();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            var input = FFMpegArguments.FromFileInput(path, false, o => o.WithCustomArgument(
                "-hide_banner -nostdin -nostats -xerror -protocol_whitelist file,pipe -err_detect explode"));
            string outputArguments = "-map 0:a:0 " + arguments;
            var processor = output == null
                ? input.OutputToNull(o => o.WithCustomArgument(outputArguments))
                : input.OutputToFile(output, false, o => o.WithCustomArgument(outputArguments));
            var result = await processor.NotifyOnStandardOutput(line => stdout.AppendLine(line))
                .CancellableThrough(timeout.Token, TimeSpan.Zero)
                .ProcessAsynchronously(throwOnError: false, ffOptions: options);
            timeout.Token.ThrowIfCancellationRequested();
            string log = string.Join('\n', result.StandardError).Trim();
            if (!result.Success)
                throw new InvalidDataException($"FFmpeg failed for '{Path.GetFileName(path)}': {log[Math.Max(0, log.Length - 1200)..]}");
            return (stdout.ToString(), log);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("FFmpeg audio gain processing exceeded 30 minutes.");
        }
    }
}
