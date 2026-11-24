using System.Buffers.Binary;
using KaraokeToolkit.Core.Archives;

namespace KaraokeToolkit.Core.Conversion;

/// <summary>Checks payload boundaries within an already identified KMA layout.</summary>
internal static class KmaPayloadChecks
{
    internal const int MaximumPadding = 64 * 1024;
    private const int SearchBufferSize = 64 * 1024;
    private const int AsfObjectHeaderSize = 24;
    private static readonly Guid AsfHeader = new("75b22630-668e-11cf-a6d9-00aa0062ce6c");
    private static readonly Guid AsfFileProperties = new("8cabdca1-a947-11cf-8ee4-00c00c205365");

    internal static async Task<long> FindPayloadStartAsync(Stream input, string extension, CancellationToken token)
    {
        long start = input.Position;
        byte[] prefix = new byte[(int)Math.Min(input.Length - start, MaximumPadding + 4)];
        await input.ReadExactlyAsync(prefix, token);
        int padding = prefix.AsSpan().IndexOfAnyExcept((byte)0);
        if (padding == 0) return start;

        if (padding > 0 && padding <= MaximumPadding)
        {
            var signature = prefix.AsSpan(padding);
            bool expected = extension == ".zip" ? signature.StartsWith("PK\x03\x04"u8)
                : signature.StartsWith("ID3"u8) || ReadMp3FrameLength(signature) > 0;
            if (expected) return start + padding;
        }
        throw new InvalidDataException("KMA payload signature was not found after supported zero padding.");
    }

    internal static async Task<long> FindSeparatorAsync(Stream input, long audioStart, string extension, CancellationToken token)
    {
        input.Position = audioStart;
        byte[] buffer = new byte[SearchBufferSize];
        long separatorOffset = -1;
        int overlap = KmaConverter.Separator.Length - 1;
        while (input.Length - input.Position >= KmaConverter.Separator.Length)
        {
            long start = input.Position;
            int count = (int)Math.Min(buffer.Length, input.Length - start);
            await input.ReadExactlyAsync(buffer.AsMemory(0, count), token);
            int searchStart = 0;
            while (searchStart <= count - KmaConverter.Separator.Length)
            {
                int relativeOffset = buffer.AsSpan(searchStart, count - searchStart).IndexOf(KmaConverter.Separator);
                if (relativeOffset < 0) break;
                int match = searchStart + relativeOffset;
                long offset = start + match;
                if (HasAlignedGraphics(input, audioStart, offset))
                {
                    if (separatorOffset >= 0)
                        return await ResolveCompetingSeparatorsAsync(input, audioStart, extension, token);
                    separatorOffset = offset;
                }
                searchStart = match + 1;
            }
            if (input.Position == input.Length) break;
            input.Position -= overlap;
        }
        if (separatorOffset < 0) throw new InvalidDataException("KMA audio/CDG separator is missing or the CDG payload is truncated.");
        if (await FindAudioEndAsync(input, audioStart, extension, token) > separatorOffset)
            throw new InvalidDataException("KMA audio/CDG separator lies inside the audio stream.");
        return separatorOffset;
    }

    private static async Task<long> ResolveCompetingSeparatorsAsync(Stream input, long audioStart, string extension, CancellationToken token)
    {
        // A decoder can accept trailing junk. When markers compete, use container
        // framing to establish the audio end before decoding the selected output.
        long? audioEnd = await FindAudioEndAsync(input, audioStart, extension, token);
        if (audioEnd is long offset && HasAlignedGraphics(input, audioStart, offset))
        {
            input.Position = offset;
            byte[] marker = new byte[KmaConverter.Separator.Length];
            await input.ReadExactlyAsync(marker, token);
            if (marker.AsSpan().SequenceEqual(KmaConverter.Separator) &&
                await KmaGraphics.IsPlausibleAsync(input, input.Position, token))
                return offset;
        }
        throw new InvalidDataException("KMA has multiple possible audio/CDG separators and no uniquely verified split.");
    }

    private static Task<long?> FindAudioEndAsync(Stream input, long start, string extension, CancellationToken token) =>
        extension == ".mp3" ? FindMp3EndAsync(input, start, token) : FindAsfEndAsync(input, start, token);

    private static bool HasAlignedGraphics(Stream input, long audioStart, long offset)
    {
        long graphicsSize = input.Length - offset - KmaConverter.Separator.Length;
        return offset > audioStart && graphicsSize > 0 && graphicsSize % KaraokeArchive.CdgPacketSize == 0;
    }

    private static async Task<long?> FindMp3EndAsync(Stream input, long start, CancellationToken token)
    {
        if (input.Length - start < 10) return null;
        byte[] header = new byte[10];
        input.Position = start;
        await input.ReadExactlyAsync(header, token);
        long frame = start;
        if (header.AsSpan().StartsWith("ID3"u8))
        {
            if (header[3] is < 2 or > 4 || header.AsSpan(6, 4).IndexOfAnyInRange((byte)128, (byte)255) >= 0)
                return null;
            int tagSize = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
            frame += 10L + tagSize + (header[3] == 4 && (header[5] & 0x10) != 0 ? 10 : 0);
        }

        int frameCount = 0;
        int versionAndRate = -1;
        while (input.Length - frame >= 4)
        {
            input.Position = frame;
            await input.ReadExactlyAsync(header.AsMemory(0, 4), token);
            int frameLength = ReadMp3FrameLength(header);
            if (frameLength == 0) break;
            int format = ((header[1] & 0x18) << 1) | (header[2] & 0x0c);
            if ((frameCount > 0 && format != versionAndRate) || frameLength > input.Length - frame) return null;
            versionAndRate = format;
            frame += frameLength;
            frameCount++;
        }
        if (frameCount < 2) return null;
        if (input.Length - frame >= 128 && header.AsSpan(0, 3).SequenceEqual("TAG"u8)) frame += 128;
        return frame;
    }

    private static int ReadMp3FrameLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4 || header[0] != 0xff || (header[1] & 0xe6) != 0xe2) return 0;
        int version = (header[1] >> 3) & 3;
        int bitrateIndex = header[2] >> 4;
        int rateIndex = (header[2] >> 2) & 3;
        if (version == 1 || bitrateIndex is 0 or 15 || rateIndex == 3) return 0;
        ReadOnlySpan<int> sampleRates = [44_100, 48_000, 32_000];
        ReadOnlySpan<int> mpeg1Bitrates = [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
        ReadOnlySpan<int> lowerVersionBitrates = [8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
        int sampleRate = sampleRates[rateIndex] >> (version == 3 ? 0 : version == 2 ? 1 : 2);
        int bitrate = (version == 3 ? mpeg1Bitrates : lowerVersionBitrates)[bitrateIndex - 1];
        return (version == 3 ? 144_000 : 72_000) * bitrate / sampleRate + ((header[2] >> 1) & 1);
    }

    private static async Task<long?> FindAsfEndAsync(Stream input, long start, CancellationToken token)
    {
        const int headerSize = 30;
        if (input.Length - start < headerSize) return null;
        byte[] header = new byte[headerSize];
        input.Position = start;
        await input.ReadExactlyAsync(header, token);
        if (new Guid(header.AsSpan(0, 16)) != AsfHeader) return null;
        long size = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16));
        if (size < headerSize || size > input.Length - start) return null;

        long endOfHeader = start + size;
        while (endOfHeader - input.Position >= AsfObjectHeaderSize)
        {
            long objectStart = input.Position;
            await input.ReadExactlyAsync(header.AsMemory(0, AsfObjectHeaderSize), token);
            long objectSize = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16));
            if (objectSize < AsfObjectHeaderSize || objectSize > endOfHeader - objectStart) return null;
            if (new Guid(header.AsSpan(0, 16)) == AsfFileProperties)
            {
                if (objectSize < 104) return null;
                input.Position += 16; // File ID precedes the eight-byte file size.
                await input.ReadExactlyAsync(header.AsMemory(0, 8), token);
                long fileSize = BinaryPrimitives.ReadInt64LittleEndian(header);
                return fileSize > size && fileSize <= input.Length - start ? start + fileSize : null;
            }
            input.Position = objectStart + objectSize;
        }
        return null;
    }
}
