using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.App.Features.Main;
using System.IO.Compression;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using SharpCompress.Writers.Zip;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.Archives;

public class ZipRepackTests
{
    private static string Seed(string folder, ZipAlgorithm algorithm = ZipAlgorithm.BZip2, string memberName = "nested/André.mp3")
    {
        string source = Path.Combine(folder, "song.zip");
        using var output = File.Create(source);
        using var writer = new ZipWriter(output, KaraokeZipWriter.CreateOptions(ZipCompression.Balanced, algorithm));
        writer.WriteDirectory("empty/", null);
        using (var member = writer.WriteToStream(memberName, new ZipWriterEntryOptions())) member.Write("audio payload"u8);
        using (var member = writer.WriteToStream("nested/André.cdg", new ZipWriterEntryOptions())) member.Write("graphics payload"u8);
        using (var member = writer.WriteToStream("zero.txt", new ZipWriterEntryOptions())) { }
        return source;
    }

    private static string Target(string source) => Path.Combine(Path.GetDirectoryName(source)!, "song (repacked).zip");
    private static Task<ConversionResult> Run(string source, ConversionOptions? options = null, OverwritePolicy policy = OverwritePolicy.Fail)
        => new SelectionConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(
            new(Action.RepackZip, [source], options ?? new() { DeleteOldFiles = false }, policy) { ValidateBeforeConversion = true });

    [Theory]
    [InlineData(OverwritePolicy.Fail)]
    [InlineData(OverwritePolicy.Skip)]
    [InlineData(OverwritePolicy.Replace)]
    [InlineData(OverwritePolicy.Ask)]
    public async Task DeleteOriginalsReplacesSourceUnderOriginalNameWithoutPrompt(OverwritePolicy policy)
    {
        string source = Seed(TestMediaFixture.NewFolder());
        byte[] original = File.ReadAllBytes(source);
        File.WriteAllText(Target(source), "unrelated existing repack");
        var result = await Run(source, new() { DeleteOldFiles = true }, policy);
        Assert.True(result.Succeeded == 1, string.Join(";", result.Files.Select(f => f.Message)));
        Assert.False(original.SequenceEqual(File.ReadAllBytes(source)));
        Assert.Equal("unrelated existing repack", File.ReadAllText(Target(source)));
        using var zip = ZipFile.OpenRead(source);
        Assert.Equal(4, zip.Entries.Count);
        using var reader = new StreamReader(zip.GetEntry("nested/André.mp3")!.Open());
        Assert.Equal("audio payload", await reader.ReadToEndAsync());
        using var archive = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(source);
        Assert.Equal(SharpCompress.Common.CompressionType.Deflate, archive.Entries.Single(e => e.Key == "nested/André.mp3").CompressionType);
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(source)!, ".karaoke-job-*"));
    }

    [Theory]
    [InlineData(ZipAlgorithm.BZip2)]
    [InlineData(ZipAlgorithm.Lzma)]
    [InlineData(ZipAlgorithm.Deflate)]
    public async Task RebuildsAsDeflateWithoutChangingContentsOrDeletingSource(ZipAlgorithm sourceAlgorithm)
    {
        string source = Seed(TestMediaFixture.NewFolder(), sourceAlgorithm);
        byte[] original = File.ReadAllBytes(source);
        var result = await Run(source); // Keep originals; automatic CUE/TXT generation does not apply.
        Assert.True(result.Succeeded == 1, string.Join(";", result.Files.Select(f => f.Message)));
        Assert.Equal(original, File.ReadAllBytes(source));
        using var zip = ZipFile.OpenRead(Target(source));
        Assert.Equal(4, zip.Entries.Count);
        Assert.NotNull(zip.GetEntry("empty/"));
        Assert.Equal(0, zip.GetEntry("zero.txt")!.Length);
        using var audio = new StreamReader(zip.GetEntry("nested/André.mp3")!.Open());
        Assert.Equal("audio payload", await audio.ReadToEndAsync());
        using var graphics = new StreamReader(zip.GetEntry("nested/André.cdg")!.Open());
        Assert.Equal("graphics payload", await graphics.ReadToEndAsync());
        using var archive = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(Target(source));
        Assert.Equal(SharpCompress.Common.CompressionType.Deflate, archive.Entries.Single(e => e.Key == "nested/André.mp3").CompressionType);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, "*.cue"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, "*.txt"));
    }

    [Theory]
    [InlineData(ZipAlgorithm.BZip2, ZipCompression.Balanced, SharpCompress.Common.CompressionType.BZip2)]
    [InlineData(ZipAlgorithm.Lzma, ZipCompression.Balanced, SharpCompress.Common.CompressionType.LZMA)]
    [InlineData(ZipAlgorithm.Deflate, ZipCompression.NoCompression, SharpCompress.Common.CompressionType.None)]
    public async Task HonorsSavedZipSettings(ZipAlgorithm algorithm, ZipCompression level, SharpCompress.Common.CompressionType expected)
    {
        string source = Seed(TestMediaFixture.NewFolder());
        var result = await Run(source, new() { ZipAlgorithm = algorithm, ZipCompression = level, DeleteOldFiles = false });
        Assert.True(result.Succeeded == 1, string.Join(";", result.Files.Select(f => f.Message)));
        using var zip = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(Target(source));
        Assert.Equal(expected, zip.Entries.Single(e => e.Key == "nested/André.mp3").CompressionType);
    }

    [Theory]
    [InlineData(OverwritePolicy.Fail, 0)]
    [InlineData(OverwritePolicy.Skip, 0)]
    [InlineData(OverwritePolicy.Replace, 1)]
    public async Task ExistingOutputFollowsPolicyAndAlwaysKeepsOriginal(OverwritePolicy policy, int succeeded)
    {
        string source = Seed(TestMediaFixture.NewFolder());
        File.WriteAllText(Target(source), "existing");
        var result = await Run(source, policy: policy);
        Assert.Equal(succeeded, result.Succeeded);
        Assert.True(File.Exists(source));
        if (succeeded == 0) Assert.Equal("existing", File.ReadAllText(Target(source)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptCentralOrLocalCrcFailsWithoutPublishing(bool local)
    {
        string source = Seed(TestMediaFixture.NewFolder(), ZipAlgorithm.Deflate);
        byte[] bytes = File.ReadAllBytes(source);
        uint signature = local ? 0x04034b50u : 0x02014b50u;
        // The first header describes an empty directory; corrupt the first file instead.
        int header = Enumerable.Range(0, bytes.Length - 4).Where(i => BitConverter.ToUInt32(bytes, i) == signature).Skip(1).First();
        bytes[header + (local ? 14 : 16)] ^= 1;
        File.WriteAllBytes(source, bytes);
        var result = await Run(source, new() { DeleteOldFiles = true });
        Assert.Equal(1, result.Failed);
        Assert.False(File.Exists(Target(source)));
        Assert.Equal(bytes, File.ReadAllBytes(source));
    }

    [Fact]
    public async Task UnsafeMemberFailsWithoutPublishing()
    {
        string source = Seed(TestMediaFixture.NewFolder(), memberName: "../outside.mp3");
        Assert.Equal(1, (await Run(source)).Failed);
        Assert.False(File.Exists(Target(source)));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task DuplicateMemberFailsAndCanBeRetriedAfterInputIsFixed()
    {
        string source = Seed(TestMediaFixture.NewFolder(), memberName: "zero.txt");
        var result = await Run(source);
        Assert.Equal(1, result.Failed);
        Assert.False(File.Exists(Target(source)));
        Seed(Path.GetDirectoryName(source)!);
        var retry = await new SelectionConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(Assert.Single(result.RetryRequests));
        Assert.Equal(1, retry.Succeeded);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task CancelledRunKeepsSourceAndPublishesNothing()
    {
        string source = Seed(TestMediaFixture.NewFolder());
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        using var cancellation = new CancellationTokenSource();
        var progress = new SelectionConversionService.InlineProgress(p => { if (p.Message.StartsWith("Repacking ZIP:")) cancellation.Cancel(); });
        var result = await new SelectionConversionService(store).RunAsync(new(Action.RepackZip, [source], new(), OverwritePolicy.Fail), progress, cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.False(File.Exists(Target(source)));
        Assert.True(File.Exists(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedRunResumesWithItsSavedSettings(bool deleteOriginals)
    {
        string source = Seed(TestMediaFixture.NewFolder());
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        using (var session = new RunSession(store, new(Action.RepackZip, Path.GetDirectoryName(source)!, new() { ZipAlgorithm = ZipAlgorithm.Lzma, DeleteOldFiles = deleteOriginals }), OverwritePolicy.Fail, [source])) { }
        Assert.Equal(1, (await new ConversionService(store).ResumeAsync(Assert.Single(store.Find()))).Succeeded);
        Assert.Empty(store.Find());
        Assert.True(File.Exists(source));
        Assert.Equal(!deleteOriginals, File.Exists(Target(source)));
        using var zip = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(deleteOriginals ? source : Target(source));
        Assert.Equal(SharpCompress.Common.CompressionType.LZMA, zip.Entries.Single(e => e.Key == "nested/André.mp3").CompressionType);
    }

    [Fact]
    public void FolderSelectionScopesAndGuiChoiceArePreserved()
    {
        string folder = TestMediaFixture.NewFolder(), source = Seed(folder);
        string child = Directory.CreateDirectory(Path.Combine(folder, "child")).FullName;
        Seed(child);
        Assert.Single(InputSelection.Plan(Action.RepackZip, [folder], false).SelectMany(b => b.Sources));
        Assert.Equal(2, InputSelection.Plan(Action.RepackZip, [folder], true).SelectMany(b => b.Sources).Count());
        var model = new KaraokeToolkit.App.Features.Main.MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        model.SelectedOperation = model.OperationSections.Single(section => section.Section == OperationSection.Archives).Operations.Single(o => o.Action == Action.RepackZip);
        model.SetInputs([source]);
        Assert.Equal(Action.RepackZip, model.SelectedOperation.Action);
        Assert.Contains("Deflate", model.SelectedOperation.Description);
    }
}
