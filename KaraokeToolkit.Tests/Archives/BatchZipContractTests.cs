using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using System.IO.Compression;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.Archives;

public class BatchZipContractTests
{
    private static readonly string[] Formats = ["mp3", "wav", "flac", "ape", "wma", "ogg"];
    private static readonly byte[] Graphics = Enumerable.Range(0, 96).Select(i => (byte)(i % 64)).ToArray();
    private static byte[] Audio(string format) => Enumerable.Repeat((byte)format.Length, 1000).ToArray();
    private static void Pair(string folder, string name, string format)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + "." + format), Audio(format));
        File.WriteAllBytes(Path.Combine(folder, name + ".cdg"), Graphics);
    }
    private static void Archive(string folder, string name, string format)
    {
        Directory.CreateDirectory(folder);
        using var zip = ZipFile.Open(Path.Combine(folder, name + ".zip"), ZipArchiveMode.Create);
        using (var audio = zip.CreateEntry("inside." + format).Open()) audio.Write(Audio(format));
        using (var cdg = zip.CreateEntry("inside.cdg").Open()) cdg.Write(Graphics);
    }
    private static byte[] Read(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }

    [Theory]
    [InlineData("nonrecursive-store", false, false, false, false, false)]
    [InlineData("recursive-delete", true, true, false, false, false)]
    [InlineData("cue-text", true, false, true, false, false)]
    [InlineData("cancel", true, false, false, true, false)]
    [InlineData("empty", true, false, true, false, true)]
    public async Task CreateBatchHonoursCompressionScopeDeletionMetadataAndCancellation(string scenario,
        bool recurse, bool delete, bool metadata, bool cancel, bool empty)
    {
        string folder = TestMediaFixture.NewFolder(), child = Path.Combine(folder, "subfolder");
        if (!empty)
        {
            foreach (string format in Formats) Pair(folder, format + " song", format);
            Pair(child, "nested song", "mp3");
            // Multiple same-named audio inputs are rejected without choosing or deleting either one.
            Pair(folder, "ambiguous song", "ogg"); File.WriteAllBytes(Path.Combine(folder, "ambiguous song.mp3"), [9, 8, 7]);
            File.WriteAllBytes(Path.Combine(folder, "unrelated.bin"), new byte[2448]);
        }
        var before = TestMediaFixture.Snapshot(folder);
        using var cancellation = new CancellationTokenSource();
        var events = new List<ConversionProgress>();
        var progress = new SynchronousProgress<ConversionProgress>(p => { events.Add(p); if (cancel && p.Message.StartsWith("Processing:")) cancellation.Cancel(); });
        var options = new ConversionOptions
        {
            RecurseFolders = recurse, DeleteOldFiles = delete, AutoCreateCueText = metadata,
            ZipCompression = scenario == "nonrecursive-store" ? ZipCompression.NoCompression : ZipCompression.Balanced
        };
        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(
            new(Action.AudioGToZip, folder, options), progress: progress, cancellationToken: cancellation.Token);
        Assert.Equal(empty ? 0 : recurse ? 8 : 7, result.Found);
        Assert.Equal(result.Found, events[0].Found);
        if (cancel || empty)
        {
            Assert.Equal(cancel, result.Cancelled); Assert.Equal(0, result.Succeeded);
            Assert.Equal(before, TestMediaFixture.Snapshot(folder)); return;
        }
        Assert.False(result.Cancelled);
        Assert.True(result.Failed == 1, string.Join("; ", result.Files.Where(f => f.Status == ConversionStatus.Failed).Select(f => f.Source + ": " + f.Message)));
        Assert.Equal(recurse ? 7 : 6, result.Succeeded);
        Assert.Contains("exactly one", result.Files.Single(f => f.Status == ConversionStatus.Failed).Message);
        foreach (string format in Formats)
        {
            string name = format + " song", path = Path.Combine(folder, name + ".zip");
            using var zip = ZipFile.OpenRead(path);
            Assert.Equal(new[] { name + ".cdg", name + "." + format }.Order(), zip.Entries.Select(e => e.FullName).Order());
            Assert.Equal(Audio(format), Read(zip, name + "." + format)); Assert.Equal(Graphics, Read(zip, name + ".cdg"));
            Assert.Equal(scenario == "nonrecursive-store" ? 0 : 8, BitConverter.ToUInt16(File.ReadAllBytes(path), 8));
            Assert.Equal(!delete, File.Exists(Path.Combine(folder, name + "." + format)));
            Assert.Equal(!delete, File.Exists(Path.Combine(folder, name + ".cdg")));
        }
        Assert.Equal(recurse, File.Exists(Path.Combine(child, "nested song.zip")));
        Assert.Equal(!recurse || !delete, File.Exists(Path.Combine(child, "nested song.mp3")));
        Assert.False(File.Exists(Path.Combine(folder, "ambiguous song.zip")));
        Assert.Equal(Audio("ogg"), File.ReadAllBytes(Path.Combine(folder, "ambiguous song.ogg")));
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(Path.Combine(folder, "ambiguous song.mp3")));
        Assert.Equal(Graphics, File.ReadAllBytes(Path.Combine(folder, "ambiguous song.cdg")));
        Assert.Equal(new byte[2448], File.ReadAllBytes(Path.Combine(folder, "unrelated.bin")));
        Assert.Equal(metadata, Directory.GetFiles(folder, "*.txt").Length > 0);
        if (metadata) Assert.Contains("mp3 song", File.ReadAllText(Assert.Single(Directory.GetFiles(folder, "*.txt"))), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("nonrecursive-keep", false, false, false, false, false)]
    [InlineData("recursive-delete", true, true, false, false, false)]
    [InlineData("text", true, false, true, false, false)]
    [InlineData("cancel", true, false, false, true, false)]
    [InlineData("empty", true, false, true, false, true)]
    public async Task ExtractBatchHonoursBasenamePayloadScopeOverwriteDeletionAndMetadata(string scenario,
        bool recurse, bool delete, bool metadata, bool cancel, bool empty)
    {
        _ = scenario;
        string folder = TestMediaFixture.NewFolder(), child = Path.Combine(folder, "subfolder");
        if (!empty)
        {
            foreach (string format in Formats) Archive(folder, format + " archive", format);
            Archive(child, "nested song", "mp3");
            File.WriteAllBytes(Path.Combine(folder, "mp3 archive.mp3"), [99]);
        }
        var before = TestMediaFixture.Snapshot(folder);
        using var cancellation = new CancellationTokenSource();
        var events = new List<ConversionProgress>();
        var progress = new SynchronousProgress<ConversionProgress>(p => { events.Add(p); if (cancel && p.Message.StartsWith("Processing:")) cancellation.Cancel(); });
        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(
            new(Action.ZipToAudioG, folder, new() { RecurseFolders = recurse, DeleteOldFiles = delete, AutoCreateCueText = metadata }),
            OverwritePolicy.Replace, progress, cancellation.Token);
        Assert.Equal(empty ? 0 : recurse ? 7 : 6, result.Found); Assert.Equal(result.Found, events[0].Found);
        if (cancel || empty)
        {
            Assert.Equal(cancel, result.Cancelled); Assert.Equal(0, result.Succeeded);
            Assert.Equal(before, TestMediaFixture.Snapshot(folder)); return;
        }
        Assert.False(result.Cancelled); Assert.Equal(0, result.Failed); Assert.Equal(recurse ? 7 : 6, result.Succeeded);
        foreach (string format in Formats)
        {
            string name = format + " archive";
            Assert.Equal(Audio(format), File.ReadAllBytes(Path.Combine(folder, name + "." + format)));
            Assert.Equal(Graphics, File.ReadAllBytes(Path.Combine(folder, name + ".cdg")));
            Assert.Equal(!delete, File.Exists(Path.Combine(folder, name + ".zip")));
        }
        Assert.Equal(recurse, File.Exists(Path.Combine(child, "nested song.mp3")));
        Assert.Equal(!recurse || !delete, File.Exists(Path.Combine(child, "nested song.zip")));
        Assert.Equal(metadata, Directory.GetFiles(folder, "*.txt").Length > 0);
        if (metadata) Assert.Contains("mp3 archive", File.ReadAllText(Assert.Single(Directory.GetFiles(folder, "*.txt"))), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*", SearchOption.AllDirectories));
    }
}
