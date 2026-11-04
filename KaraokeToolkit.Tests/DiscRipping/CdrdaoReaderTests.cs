using KaraokeToolkit.Core.Audio;
using System.Diagnostics;
using System.IO.Compression;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.DiscRipping;

namespace KaraokeToolkit.Tests.DiscRipping;

public class CdrdaoReaderTests
{
    // Canonical CD+G sector layout used by the synthetic image and expected reader:
    // 2352 little-endian PCM bytes followed by 96 six-bit CDG symbols per sector.
    private const string Toc = "CD_DA\nTRACK AUDIO RW_RAW\nDATAFILE \"disc.bin\" 00:02:00\nTRACK AUDIO RW_RAW\nDATAFILE \"disc.bin\" #367200 00:02:00\n";
    private sealed class Tool
    {
        internal string Root = TestMediaFixture.NewFolder();
        internal string Mode = "ok";
        internal int Captures;
        internal string Address = "0,1,0";
        internal List<string[]> Commands = [];
        internal byte[] Image = ImageBytes();
        internal NativeDiscReader Reader() => new(Run, () => "cdrdao fixture", Root);
        private async Task<string> Run(ProcessStartInfo start, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string[] args = start.ArgumentList.ToArray(); Commands.Add(args);
            Assert.Equal("C", start.Environment["LC_ALL"]);
            if (args[0] == "scanbus") return Mode == "no-drive" ? "Cdrdao version 1.2.6\n" : $"{Address} : Vendor, CD drive, 1.0\n{Address} : Vendor, CD drive, 1.0\n";
            Assert.Contains("--read-subchan", args); Assert.Contains("rw_raw", args);
            Assert.Equal(Address, args[Array.IndexOf(args, "--device") + 1]);
            if (Mode == "error") throw new IOException("Drive denied access");
            if (args[0] == "read-cd")
            {
                Captures++;
                if (Mode == "wait") await Task.Delay(Timeout.Infinite, token);
                await File.WriteAllBytesAsync(Path.Combine(start.WorkingDirectory, "disc.bin"), Mode == "short" ? Image[..^1] : Image, token);
            }
            string toc = Mode == "changed" && args[0] == "read-cd" ? Toc.Replace("00:02:00", "00:03:00") : Toc;
            if (Mode == "unsupported") toc = toc.Replace(" RW_RAW", "");
            if (Mode != "missing") await File.WriteAllTextAsync(Path.Combine(start.WorkingDirectory, "disc.toc"), toc, token);
            return "Reading...\n";
        }
    }
    private sealed class ExpectedSectorReader : IDiscReader
    {
        private readonly DiscRipTests.FakeReader original = new();
        internal OpticalDisc Disc => original.Disc;
        public Task<IReadOnlyList<OpticalDisc>> ScanAsync(CancellationToken token) => original.ScanAsync(token);
        public async Task ReadTrackAsync(OpticalDisc disc, DiscTrack track, string bin, IProgress<int>? progress, CancellationToken token)
        {
            await original.ReadTrackAsync(disc, track, bin, progress, token);
            if (track.Number == 2)
            {
                byte[] data = await File.ReadAllBytesAsync(bin, token);
                for (int s = 148; s < 150; s++) Array.Clear(data, s * 2448 + 2352, 96);
                await File.WriteAllBytesAsync(bin, data, token);
            }
        }
    }
    private static byte[] ImageBytes()
    {
        byte[] image = new byte[300 * 2448];
        // Encode packet columns into their delayed raw positions, independently of extraction.
        int[] delays = [0, 66, 125, 191, 100, 50, 150, 175, 8, 33, 58, 83, 108, 133, 158, 183, 16, 41, 25, 91, 116, 141, 166, 75];
        for (int s = 0; s < 298; s++)
            for (int p = 0; p < 4; p++)
                for (int c = 0; c < 24; c++)
                {
                    int position = s * 96 + p * 24 + delays[c];
                    image[(position / 96) * 2448 + 2352 + position % 96] = (byte)(0xc0 | ((p * 24 + c) % 64));
                }
        return image;
    }

    [Theory]
    [InlineData("0,1,0")]
    [InlineData("ATA:0,1,0")]
    [InlineData("/dev/sr0")]
    [InlineData("IOService:/AppleACPIPlatform/USB Drive/Optical")]
    public async Task DiscoveryReadsStderrAddressesAndDeduplicatesDrives(string address)
    {
        var tool = new Tool { Address = address };
        var disc = Assert.Single(await tool.Reader().ScanAsync(default));
        Assert.True(disc.SupportsCdg); Assert.Equal(address, disc.Device);
        Assert.Equal(new[] { 1, 2 }, disc.Tracks.Select(t => t.Number));
        Assert.All(disc.Tracks, t => Assert.Equal(150 * 2352, t.AudioBytes));
        Assert.Empty(Directory.GetDirectories(tool.Root));
    }

    [Theory]
    [InlineData(RipFormat.Bin)]
    [InlineData(RipFormat.AudioG)]
    [InlineData(RipFormat.Zip)]
    public async Task OutputsMatchSyntheticSectorPayloadsThroughRealPipeline(RipFormat format)
    {
        var tool = new Tool(); var reader = tool.Reader();
        var disc = Assert.Single(await reader.ScanAsync(default));
        string modern = TestMediaFixture.NewFolder(), reference = TestMediaFixture.NewFolder();
        var options = new ConversionOptions { ExperimentalDiscRipping = true, AudioEncoder = AudioEncoder.WAV, AutoCreateCueText = false };
        var result = await new DiscRipService(reader).RunAsync(new(disc, [1, 2], modern, "DISC", format, options), OverwritePolicy.Fail);
        Assert.True(result.Succeeded == 2, string.Join(";", result.Files.Select(f => f.Message)));
        var expected = new ExpectedSectorReader();
        Assert.Equal(2, (await new DiscRipService(expected).RunAsync(new(expected.Disc, [1, 2], reference, "DISC", format, options), OverwritePolicy.Fail)).Succeeded);
        Assert.Equal(1, tool.Captures); // Full disc is captured only once for both selected tracks.
        foreach (string file in Directory.GetFiles(reference))
        {
            string target = Path.Combine(modern, Path.GetFileName(file));
            if (format != RipFormat.Zip) Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(target));
            else
            {
                using var oldZip = ZipFile.OpenRead(file); using var newZip = ZipFile.OpenRead(target);
                foreach (var entry in oldZip.Entries)
                {
                    using var oldStream = entry.Open(); using var newStream = newZip.GetEntry(entry.FullName)!.Open();
                    using var oldBytes = new MemoryStream(); using var newBytes = new MemoryStream();
                    await oldStream.CopyToAsync(oldBytes); await newStream.CopyToAsync(newBytes);
                    Assert.Equal(oldBytes.ToArray(), newBytes.ToArray());
                }
            }
        }
        Assert.Empty(Directory.GetDirectories(tool.Root));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("short")]
    [InlineData("missing")]
    [InlineData("error")]
    public async Task BadCaptureCannotOverwriteExistingOutput(string mode)
    {
        var tool = new Tool(); var reader = tool.Reader();
        var disc = Assert.Single(await reader.ScanAsync(default)); tool.Mode = mode;
        string folder = TestMediaFixture.NewFolder(), target = Path.Combine(folder, "DISC-01.bin"); File.WriteAllText(target, "original");
        var result = await new DiscRipService(reader).RunAsync(new(disc, [1], folder, "DISC", RipFormat.Bin, new() { ExperimentalDiscRipping = true }), OverwritePolicy.Replace);
        Assert.Equal(1, result.Failed); Assert.Equal("original", File.ReadAllText(target));
        Assert.Empty(Directory.GetDirectories(tool.Root)); Assert.Empty(Directory.GetDirectories(folder));
    }

    [Fact]
    public async Task NoDrivesAndUnsupportedSubchannelsAreReported()
    {
        var tool = new Tool { Mode = "no-drive" }; var reader = tool.Reader();
        Assert.Empty(await reader.ScanAsync(default));
        tool.Mode = "unsupported";
        Assert.False(Assert.Single(await reader.ScanAsync(default)).SupportsCdg);
        Assert.Empty(Directory.GetDirectories(tool.Root));
    }

    [Fact]
    public async Task CancelledCaptureCleansImageAndPublishesNothing()
    {
        var tool = new Tool(); var reader = tool.Reader(); var disc = Assert.Single(await reader.ScanAsync(default));
        tool.Mode = "wait";
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        string folder = TestMediaFixture.NewFolder();
        var result = await new DiscRipService(reader).RunAsync(new(disc, [1, 2], folder, "DISC", RipFormat.Bin, new() { ExperimentalDiscRipping = true }), OverwritePolicy.Fail, token: cancel.Token);
        Assert.True(result.Cancelled); Assert.Equal(1, tool.Captures);
        Assert.Empty(Directory.GetFiles(folder)); Assert.Empty(Directory.GetDirectories(tool.Root));
    }

    [Fact]
    public async Task PcmEndianConversionTrackOffsetsAndPregapAreExact()
    {
        string folder = TestMediaFixture.NewFolder(), image = Path.Combine(folder, "disc.bin"), bin = Path.Combine(folder, "track.bin");
        byte[] input = new byte[6 * 2448];
        for (int s = 0; s < 6; s++)
        {
            for (int i = 0; i < 2352; i += 2) { input[s * 2448 + i] = (byte)s; input[s * 2448 + i + 1] = (byte)(i % 251); }
            for (int i = 0; i < 96; i++) input[s * 2448 + 2352 + i] = (byte)(0xc0 | (i % 64));
        }
        File.WriteAllBytes(image, input);
        await CdrdaoToc.ExtractAsync(image, new(2, 2448, 3, 1, true, true), bin, null, default);
        byte[] output = File.ReadAllBytes(bin); Assert.Equal(2 * 2448, output.Length);
        Assert.Equal(0, output[0]); Assert.Equal(2, output[1]); Assert.Equal(2, output[2]); Assert.Equal(2, output[3]);
        Assert.Equal(3, output[2449]);
        Assert.Equal(0, output[2352]); Assert.Equal(2, output[2353]); Assert.Equal(29, output[2354]); // Raw columns 0, 66 and 125 from lookahead.
    }

    [Theory]
    [InlineData("TRACK AUDIO RW_RAW\nDATAFILE \"../outside.bin\" 00:01:00")]
    [InlineData("TRACK AUDIO RW_RAW\nDATAFILE \"disc.bin\" #1 00:01:00")]
    [InlineData("TRACK AUDIO RW_RAW\nDATAFILE \"disc.bin\" 00:60:00")]
    [InlineData("TRACK AUDIO\nFILE \"disc.bin\" 0 00:01:00")]
    [InlineData("TRACK MODE1_RAW RW_RAW\nDATAFILE \"disc.bin\" 00:01:00")]
    [InlineData("TRACK AUDIO RW_RAW\nDATAFILE \"disc.bin\" 00:01:00\nSTART 00:01:00")]
    public void UnsupportedTocIsRejected(string toc) => Assert.ThrowsAny<Exception>(() => CdrdaoToc.Parse(toc));
}
