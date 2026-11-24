using System.Buffers.Text;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;

namespace KaraokeToolkit.Core.Conversion;

/// <summary>Extracts the karaoke payload from a staged KMA and prepares an MP3+G pair.</summary>
internal sealed class KmaConverter(
    Func<string, TimeSpan, CancellationToken, Task<string?>> checkGraphicsAndSync,
    Func<Stream, long, string, CancellationToken, Task<long>> findSeparator,
    Func<Stream, string, CancellationToken, Task<long>> findPayloadStart)
{
    private enum PayloadLayout { InterleavedWmaAndCdg, SeparatedWmaAndCdg, SeparatedMp3AndCdg, KaraokeZip }

    private const int HeaderSize = 256;
    private const int HeaderTypeMarkerOffset = 165;
    private const int HeaderTypeOffset = 169;
    private const int InterleaveSize = 31_824;
    private const int BufferSize = 64 * 1024;
    internal static ReadOnlySpan<byte> Separator => "FEEDFACE"u8;

    internal async Task<(string Mp3, string Cdg, string? Warning)> ConvertAsync(string source, int bitrate, CancellationToken token)
    {
        string mp3 = Path.ChangeExtension(source, ".mp3");
        string cdg = Path.ChangeExtension(source, ".cdg");
        string audio;
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
        {
            if (input.Length <= HeaderSize) throw new InvalidDataException("KMA is missing its header or payload.");
            byte[] header = new byte[HeaderSize];
            await input.ReadExactlyAsync(header, token);
            var layout = ReadLayout(header);
            if (layout == PayloadLayout.KaraokeZip)
            {
                // ZIP offsets are relative to the archive, so expose it as its own staged file.
                string zip = Path.ChangeExtension(source, ".zip");
                input.Position = await findPayloadStart(input, ".zip", token);
                await using (var archive = OpenOutput(zip))
                    await input.CopyToAsync(archive, token);
                (audio, cdg) = await KaraokeArchive.ExtractAsync(zip, token);
                if (Path.GetExtension(audio) is not (".mp3" or ".wma" or ".wav"))
                    throw new NotSupportedException("Embedded KMA ZIP audio must be MP3, WMA or WAV.");
            }
            else
            {
                bool interleaved = layout == PayloadLayout.InterleavedWmaAndCdg;
                audio = layout == PayloadLayout.SeparatedMp3AndCdg ? mp3 : Path.ChangeExtension(source, ".wma");
                await using var audioOutput = OpenOutput(audio);
                await using var graphicsOutput = OpenOutput(cdg);
                if (interleaved)
                    await ExtractInterleavedAsync(input, header, audioOutput, graphicsOutput, token);
                else
                {
                    string extension = Path.GetExtension(audio);
                    long start = extension == ".mp3" ? await findPayloadStart(input, extension, token) : input.Position;
                    long boundary = await findSeparator(input, start, extension, token);
                    await ExtractSeparatedAsync(input, start, boundary, audioOutput, graphicsOutput, token);
                }
            }
        }

        var duration = await KmaAudio.PrepareMp3Async(audio, mp3, bitrate, token);
        return (mp3, cdg, await checkGraphicsAndSync(cdg, duration, token));
    }

    private static PayloadLayout ReadLayout(byte[] header)
    {
        if (header[HeaderTypeMarkerOffset] != (byte)'@') return PayloadLayout.InterleavedWmaAndCdg;
        return header[HeaderTypeOffset] switch
        {
            1 => PayloadLayout.InterleavedWmaAndCdg,
            // These two import types share the same on-disk payload layout.
            3 or 4 => PayloadLayout.SeparatedWmaAndCdg,
            5 => PayloadLayout.SeparatedMp3AndCdg,
            6 => PayloadLayout.KaraokeZip,
            7 => throw new NotSupportedException("Audio-only KMA contains no CDG graphics."),
            var type => throw new NotSupportedException($"Unsupported KMA header type ({type}).")
        };
    }

    private static async Task ExtractInterleavedAsync(Stream input, byte[] header, Stream audio, Stream cdg, CancellationToken token)
    {
        long audioLeft = ReadLength(header.AsSpan(240, 8));
        long graphicsLeft = ReadLength(header.AsSpan(248, 8));
        if (audioLeft <= 0 || graphicsLeft <= 0 || graphicsLeft % KaraokeArchive.CdgPacketSize != 0 ||
            audioLeft + graphicsLeft != input.Length - HeaderSize)
            throw new InvalidDataException("KMA audio and CDG lengths do not match the interleaved payload.");

        byte[] buffer = new byte[InterleaveSize];
        while (audioLeft > 0 || graphicsLeft > 0)
        {
            int graphicsCount = (int)Math.Min(InterleaveSize, graphicsLeft);
            int audioCount = (int)Math.Min(InterleaveSize, audioLeft);
            await CopyExactlyAsync(input, cdg, graphicsCount, buffer, token);
            await CopyExactlyAsync(input, audio, audioCount, buffer, token);
            graphicsLeft -= graphicsCount;
            audioLeft -= audioCount;
        }
    }

    private static int ReadLength(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        if (end >= 0) field = field[..end];
        field = field.Trim((byte)' ');
        if (Utf8Parser.TryParse(field, out int length, out int consumed) && consumed == field.Length)
            return length;
        throw new InvalidDataException("Invalid stream length in the KMA header.");
    }

    private static async Task ExtractSeparatedAsync(Stream input, long audioStart, long separatorOffset, Stream audio, Stream cdg, CancellationToken token)
    {
        byte[] buffer = new byte[BufferSize];
        input.Position = audioStart;
        await CopyExactlyAsync(input, audio, separatorOffset - audioStart, buffer, token);
        input.Position += Separator.Length;
        await CopyExactlyAsync(input, cdg, input.Length - input.Position, buffer, token);
    }

    private static async Task CopyExactlyAsync(Stream input, Stream output, long remaining, byte[] buffer, CancellationToken token)
    {
        while (remaining > 0)
        {
            int count = (int)Math.Min(remaining, buffer.Length);
            await input.ReadExactlyAsync(buffer.AsMemory(0, count), token);
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            remaining -= count;
        }
    }

    private static FileStream OpenOutput(string path) => new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
}
