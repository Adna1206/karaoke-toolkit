using KaraokeToolkit.Core.Recovery;
using System.Text;
using System.Text.RegularExpressions;

namespace KaraokeToolkit.Core.DiscRipping;

/// <summary>Reads the restricted TOC layout emitted by cdrdao read-toc/read-cd.
/// Arbitrary user TOCs, external files and synthetic silence are deliberately unsupported.</summary>
internal static class CdrdaoToc
{
    // CD R-W packet interleave positions (four 24-symbol packets per sector).
    // The mapping is also documented by cdgtools' cdgparse.Deinterleave.
    private static readonly int[] SymbolOffsets = [0, 66, 125, 191, 100, 50, 150, 175,
        8, 33, 58, 83, 108, 133, 158, 183, 16, 41, 25, 91, 116, 141, 166, 75];
    internal sealed record Track(int Number, long Offset, long Sectors, long Pregap, bool Audio, bool Raw)
    {
        internal DiscTrack AsDiscTrack() => new(Number, checked((Sectors - Pregap) * 2352));
    }
    internal static Track[] Parse(string text)
    {
        var tracks = new List<Track>();
        bool audio = false, raw = false;
        long offset = 0, sectors = 0, pregap = 0;
        bool hasTrack = false, hasData = false;
        void Finish()
        {
            if (!hasTrack) return;
            if (!hasData || sectors <= 0 || pregap >= sectors || offset < 0)
                throw new InvalidDataException("Incomplete or unsupported cdrdao track layout.");
            tracks.Add(new(tracks.Count + 1, offset, sectors, pregap, audio, raw));
        }
        foreach (string original in text.Split('\n'))
        {
            string line = Regex.Replace(original, @"\s*//.*$", "").Trim();
            if (line.StartsWith("TRACK ", StringComparison.Ordinal))
            {
                Finish();
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                audio = parts[1] == "AUDIO";
                raw = parts.Length == 3 && parts[2] == "RW_RAW";
                if (audio && !raw) throw new NotSupportedException("Disc image does not contain raw CD+G subchannels.");
                hasTrack = true; hasData = false; offset = sectors = pregap = 0;
            }
            else if (line.StartsWith("DATAFILE ", StringComparison.Ordinal))
            {
                var match = Regex.Match(line, "^DATAFILE \\\"disc\\.bin\\\"(?: #([0-9]+))? ([0-9]+:[0-9]{2}:[0-9]{2})$");
                if (!hasTrack || hasData || !match.Success) throw new InvalidDataException("Unsupported cdrdao image reference.");
                offset = match.Groups[1].Success ? long.Parse(match.Groups[1].Value) : 0;
                sectors = Frames(match.Groups[2].Value);
                if (offset % 2448 != 0) throw new InvalidDataException("Unaligned cdrdao image offset.");
                hasData = true;
            }
            else if (line.StartsWith("START ", StringComparison.Ordinal)) pregap = Frames(line[6..]);
            else if (Regex.IsMatch(line, @"^(FILE|AUDIOFILE|SILENCE|ZERO|PREGAP|FIFO)\b"))
                throw new NotSupportedException("Unsupported cdrdao track segment.");
        }
        Finish();
        if (tracks.Count == 0 || tracks.Count > 99) throw new InvalidDataException("Invalid cdrdao track count.");
        // Mixed-mode layouts may have different sector sizes. Refuse rather than guess offsets.
        if (tracks.Any(t => !t.Audio || !t.Raw)) throw new NotSupportedException("Only audio/CD+G discs are supported; mixed-mode discs are unsupported.");
        for (int i = 1; i < tracks.Count; i++)
            if (tracks[i].Offset != checked(tracks[i - 1].Offset + tracks[i - 1].Sectors * 2448))
                throw new InvalidDataException("Overlapping or discontinuous cdrdao tracks.");
        return tracks.ToArray();
    }
    internal static long Frames(string value)
    {
        var match = Regex.Match(value, @"^([0-9]+):([0-9]{2}):([0-9]{2})$");
        if (!match.Success) throw new InvalidDataException("Invalid cdrdao track time.");
        long minutes = long.Parse(match.Groups[1].Value);
        int seconds = int.Parse(match.Groups[2].Value), frames = int.Parse(match.Groups[3].Value);
        if (seconds >= 60 || frames >= 75) throw new InvalidDataException("Invalid cdrdao track time.");
        return checked((minutes * 60 + seconds) * 75 + frames);
    }
    internal static string Identity(IEnumerable<Track> tracks) => RecoveryFingerprint.HashBytes(Encoding.UTF8.GetBytes(
        string.Join(';', tracks.Select(t => $"{t.Number}:{t.Offset}:{t.Sectors}:{t.Pregap}"))));

    /// <summary>Normalizes cdrdao's big-endian PCM and raw P-W symbols to the
    /// little-endian PCM + 96 deinterleaved six-bit CDG symbols used by CD+G BIN files.
    /// Lookahead crosses track boundaries; unavailable disc-tail graphics
    /// are blanked, preserving audio length rather than inventing incomplete packets.</summary>
    internal static async Task ExtractAsync(string image, Track track, string destination, IProgress<int>? progress, CancellationToken token)
    {
        await using var input = File.OpenRead(image);
        if (input.Length % 2448 != 0 || checked(track.Offset + track.Sectors * 2448) > input.Length)
            throw new InvalidDataException("Incomplete cdrdao disc image.");
        token.ThrowIfCancellationRequested();
        input.Position = checked(track.Offset + track.Pregap * 2448);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        byte[] sector = new byte[2448];
        await using var graphics = File.OpenRead(image);
        byte[] symbols = new byte[288];
        long firstSector = input.Position / 2448;
        async Task ReadSymbols(long index, int offset)
        {
            symbols.AsSpan(offset, 96).Clear();
            if (index * 2448 >= graphics.Length) return;
            graphics.Position = index * 2448 + 2352;
            await graphics.ReadExactlyAsync(symbols.AsMemory(offset, 96), token);
        }
        for (int j = 0; j < 3; j++) await ReadSymbols(firstSector + j, j * 96);
        long count = track.Sectors - track.Pregap;
        for (long i = 0; i < count; i++)
        {
            await input.ReadExactlyAsync(sector, token);
            for (int j = 0; j < 2352; j += 2) (sector[j], sector[j + 1]) = (sector[j + 1], sector[j]);
            sector.AsSpan(2352, 96).Clear();
            if ((firstSector + i + 2) * 2448 < graphics.Length)
                for (int pack = 0; pack < 4; pack++)
                    for (int column = 0; column < 24; column++)
                        sector[2352 + pack * 24 + column] = (byte)(symbols[pack * 24 + SymbolOffsets[column]] & 0x3f);
            await output.WriteAsync(sector, token);
            progress?.Report((int)((i + 1) * 100 / count));
            if (i + 1 < count)
            {
                Array.Copy(symbols, 96, symbols, 0, 192);
                await ReadSymbols(firstSector + i + 3, 192);
            }
        }
        await output.FlushAsync(token);
    }
}
