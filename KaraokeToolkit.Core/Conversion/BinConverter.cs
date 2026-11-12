using KaraokeToolkit.Core.Archives;
namespace KaraokeToolkit.Core.Conversion;

/// <summary>Splits CD+G BIN sectors into PCM WAV audio and the unchanged 96-byte graphics blocks.</summary>
internal static class BinConverter
{
    internal const int SectorSize = 2448;
    internal static void Validate(string bin)
    {
        long length = new FileInfo(bin).Length;
        if (length == 0 || length % SectorSize != 0)
            throw new InvalidDataException("BIN must contain complete 2448-byte sectors.");
    }
    internal static async Task SplitAsync(string bin, string wav, string cdg, CancellationToken token)
    {
        Validate(bin);
        await using var input = File.OpenRead(bin);
        long pcmLength = checked(input.Length / SectorSize * 2352);
        if (pcmLength > uint.MaxValue - 36)
            throw new InvalidDataException("Audio exceeds the RIFF WAV size limit.");
        await using var audio = new FileStream(wav, FileMode.CreateNew, FileAccess.Write);
        await using var graphics = new FileStream(cdg, FileMode.CreateNew, FileAccess.Write);
        using (var writer = new BinaryWriter(audio, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write((uint)(pcmLength + 36));
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(44100);
            writer.Write(176400);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write((uint)pcmLength);
        }
        byte[] sector = new byte[SectorSize];
        while (input.Position < input.Length)
        {
            await input.ReadExactlyAsync(sector, token);
            await audio.WriteAsync(sector.AsMemory(0, 2352), token);
            await graphics.WriteAsync(sector.AsMemory(2352, 96), token);
        }
    }
    /// <summary>Interleaves all decoded audio and graphics, zero-padding the shorter stream.</summary>
    internal static async Task CombineAsync(string pcm, string cdg, string bin, CancellationToken token)
    {
        KaraokeArchive.ValidateGraphics(cdg);
        await using var audio = File.OpenRead(pcm);
        await using var graphics = File.OpenRead(cdg);
        if (audio.Length == 0 || audio.Length % 4 != 0)
            throw new InvalidDataException("PCM must contain complete stereo samples.");
        long sectors = Math.Max((audio.Length + 2351) / 2352, (graphics.Length + 95) / 96);
        await using var output = new FileStream(bin, FileMode.CreateNew, FileAccess.Write);
        byte[] sector = new byte[SectorSize];
        for (long i = 0; i < sectors; i++)
        {
            Array.Clear(sector);
            int music = (int)Math.Min(2352, audio.Length - audio.Position);
            int packets = (int)Math.Min(96, graphics.Length - graphics.Position);
            await audio.ReadExactlyAsync(sector.AsMemory(0, music), token);
            await graphics.ReadExactlyAsync(sector.AsMemory(2352, packets), token);
            await output.WriteAsync(sector, token);
        }
    }
}

