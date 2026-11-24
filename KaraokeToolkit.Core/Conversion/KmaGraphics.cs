using System.Globalization;
using KaraokeToolkit.Core.Archives;

namespace KaraokeToolkit.Core.Conversion;

internal static class KmaGraphics
{
    private const int PacketSize = KaraokeArchive.CdgPacketSize;
    private const int PacketsPerSecond = 300;
    private static readonly TimeSpan SyncWarningThreshold = TimeSpan.FromSeconds(1);

    internal static async Task<string?> CheckGraphicsAndSyncAsync(string path, TimeSpan audioDuration, CancellationToken token)
    {
        await using var input = File.OpenRead(path);
        var packets = await InspectAsync(input, 0, token);
        var warnings = new List<string>();
        if (packets.Graphics == 0)
            warnings.Add("CDG contains no recognized graphics instructions.");
        if (packets.UnknownInstructions > 0)
            warnings.Add("CDG contains unrecognized graphics instructions.");

        var graphicsDuration = TimeSpan.FromSeconds(packets.Count / (double)PacketsPerSecond);
        if ((audioDuration - graphicsDuration).Duration() > SyncWarningThreshold)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"Possible sync issue. Decoded audio is {audioDuration.TotalSeconds:F2} s and CDG is {graphicsDuration.TotalSeconds:F2} s."));
        }
        return warnings.Count == 0 ? null : string.Join(' ', warnings);
    }

    internal static async Task<bool> IsPlausibleAsync(Stream input, long offset, CancellationToken token)
    {
        var packets = await InspectAsync(input, offset, token);
        return packets.Other == 0 || packets.Graphics > packets.Other;
    }

    private static async Task<(long Count, long Graphics, long UnknownInstructions, long Other)> InspectAsync(
        Stream input, long offset, CancellationToken token)
    {
        long length = input.Length - offset;
        if (length <= 0 || length % PacketSize != 0)
            throw new InvalidDataException("CDG must contain complete 24-byte packets.");

        input.Position = offset;
        byte[] buffer = new byte[PacketSize * 2048];
        long graphics = 0, unknown = 0, other = 0;
        while (input.Position < input.Length)
        {
            int count = (int)Math.Min(buffer.Length, input.Length - input.Position);
            await input.ReadExactlyAsync(buffer.AsMemory(0, count), token);
            for (int packet = 0; packet < count; packet += PacketSize)
            {
                switch (buffer[packet] & 0x3f)
                {
                    case 0: // Empty packet, retained for timing.
                        break;
                    case 9 when (buffer[packet + 1] & 0x3f) is 1 or 2 or 6 or 20 or 24 or 28 or 30 or 31 or 38:
                        graphics++;
                        break;
                    case 9:
                        unknown++;
                        break;
                    default:
                        other++;
                        break;
                }
            }
        }
        return (length / PacketSize, graphics, unknown, other + unknown);
    }
}
