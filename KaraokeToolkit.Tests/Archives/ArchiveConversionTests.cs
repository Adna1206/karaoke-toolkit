using KaraokeToolkit.Core.Archives;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;
namespace KaraokeToolkit.Tests.Archives;

public class ArchiveConversionTests
{
    static string Seed(string folder, string extension = ".tar", string name = "nested/André.txt", bool link = false)
    {
        string path = Path.Combine(folder, "source" + extension);
        using var file = File.Create(path);
        using Stream output = extension == ".tgz" ? new GZipStream(file, CompressionLevel.Optimal, true) : file;
        using var writer = new TarWriter(output, leaveOpen: true);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "empty/"));
        var entry = new PaxTarEntry(link ? TarEntryType.SymbolicLink : TarEntryType.RegularFile, name);
        if (link) entry.LinkName = "../../outside";
        else entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("preserve this content"));
        writer.WriteEntry(entry);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "zero.txt") { DataStream = new MemoryStream() });
        return path;
    }
    static Task<ConversionResult> Run(string source, ZipAlgorithm algorithm = ZipAlgorithm.Deflate, OverwritePolicy policy = OverwritePolicy.Fail, bool delete = false, CancellationToken token = default)
        => new SelectionConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(new(Action.Archive_To_Zip, [source], new() { ZipAlgorithm = algorithm, DeleteOldFiles = delete }, policy) { ValidateBeforeConversion = true }, token: token);

    [Theory]
    [InlineData(".tar", ZipAlgorithm.Deflate)]
    [InlineData(".tgz", ZipAlgorithm.Deflate)]
    [InlineData(".tar", ZipAlgorithm.BZip2)]
    [InlineData(".tar", ZipAlgorithm.Lzma)]
    public async Task PreservesContentsAndDirectoriesUsingSelectedAlgorithm(string extension, ZipAlgorithm algorithm)
    {
        string folder = TestMediaFixture.NewFolder(), source = Seed(folder, extension);
        var result = await Run(source, algorithm);
        Assert.True(result.Succeeded == 1, string.Join(";", result.Files.Select(f => f.Message)));
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(folder, "source.txt"))); // No automatic karaoke listing for generic archives.
        using var zip = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(Path.Combine(folder, "source.zip"));
        Assert.Equal(3, zip.Entries.Count());
        Assert.True(zip.Entries.Single(e => e.Key == "empty/").IsDirectory);
        Assert.Equal(0, zip.Entries.Single(e => e.Key == "zero.txt").Size);
        var entry = zip.Entries.Single(e => e.Key == "nested/André.txt");
        using var stream = entry.OpenEntryStream();
        using var reader = new StreamReader(stream);
        Assert.Equal("preserve this content", await reader.ReadToEndAsync());
        Assert.Equal(algorithm switch { ZipAlgorithm.Deflate => SharpCompress.Common.CompressionType.Deflate, ZipAlgorithm.BZip2 => SharpCompress.Common.CompressionType.BZip2, _ => SharpCompress.Common.CompressionType.LZMA }, entry.CompressionType);
    }
    [Theory]
    [InlineData(OverwritePolicy.Fail, 0)]
    [InlineData(OverwritePolicy.Skip, 0)]
    [InlineData(OverwritePolicy.Replace, 1)]
    public async Task ExistingOutputAndDeletionFollowPolicy(OverwritePolicy policy, int succeeded)
    {
        string folder = TestMediaFixture.NewFolder(), source = Seed(folder), target = Path.Combine(folder, "source.zip");
        File.WriteAllText(target, "original");
        var result = await Run(source, policy: policy, delete: true);
        Assert.Equal(succeeded, result.Succeeded);
        Assert.Equal(succeeded == 0, File.Exists(source));
        if (succeeded == 0) Assert.Equal("original", File.ReadAllText(target));
    }
    [Theory]
    [InlineData("../outside.txt", false)]
    [InlineData("link", true)]
    public async Task UnsafeEntriesKeepSourceAndPublishNothing(string name, bool link)
    {
        string folder = TestMediaFixture.NewFolder(), source = Seed(folder, name: name, link: link);
        var result = await Run(source, delete: true);
        Assert.Equal(1, result.Failed);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(folder, "source.zip")));
    }
    [Fact]
    public async Task BrokenArchiveCanBeRetriedAndZipInputsAreExcluded()
    {
        string folder = TestMediaFixture.NewFolder(), source = Path.Combine(folder, "source.tar");
        File.WriteAllText(source, "broken");
        var result = await Run(source, delete: true);
        Assert.Equal(1, result.Failed);
        Assert.Equal(Action.Archive_To_Zip, Assert.Single(result.RetryRequests).Action);
        Assert.True(File.Exists(source));
        Seed(folder);
        var retry = await new SelectionConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(result.RetryRequests.Single());
        Assert.Equal(1, retry.Succeeded);
        Assert.Empty(InputSelection.Plan(Action.Archive_To_Zip, [folder], false));
        Assert.Throws<ArgumentException>(() => InputSelection.Plan(Action.Archive_To_Zip, [Path.Combine(folder, "source.zip")], false));
    }
    [Fact]
    public async Task PendingArchiveRunResumesWithSavedSettings()
    {
        string folder = TestMediaFixture.NewFolder(), source = Seed(folder);
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        using (var session = new RunSession(store, new(Action.Archive_To_Zip, folder, new() { ZipAlgorithm = ZipAlgorithm.BZip2, DeleteOldFiles = false }), OverwritePolicy.Fail, [source])) { }
        var job = Assert.Single(store.Find());
        var result = await new ConversionService(store).ResumeAsync(job);
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(f => f.Message)));
        Assert.Empty(store.Find());
    }
    [Fact]
    public async Task CancellationDuringStagingLeavesSourceAndNoZip()
    {
        string folder = TestMediaFixture.NewFolder(), source = Seed(folder);
        using var cancellation = new CancellationTokenSource();
        var progress = new SelectionConversionService.InlineProgress(p => { if (p.Message.StartsWith("Converting archive:")) cancellation.Cancel(); });
        var result = await new SelectionConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(new(Action.Archive_To_Zip, [source], new(), OverwritePolicy.Fail), progress, cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(folder, "source.zip")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SolidSevenZipConvertsAndEncryptedSevenZipRetainsSource(bool encrypted)
    {
        string folder = TestMediaFixture.NewFolder();
        string source = Path.Combine(folder, "source.7z");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Support", "Fixtures", encrypted ? "archive-encrypted.7z" : "archive-solid.7z"), source);
        var result = await Run(source, delete: true);
        Assert.Equal(encrypted ? 0 : 1, result.Succeeded);
        Assert.Equal(encrypted, File.Exists(source));
        Assert.Equal(!encrypted, File.Exists(Path.Combine(folder, "source.zip")));
        if (!encrypted)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(folder, "source.zip"));
            using var reader = new StreamReader(zip.GetEntry("nested/song.txt")!.Open());
            Assert.Equal("Seven zip payload", await reader.ReadToEndAsync());
            using var second = new StreamReader(zip.GetEntry("nested/second.txt")!.Open());
            Assert.Equal("Another solid member", await second.ReadToEndAsync());
        }
    }
    [Fact]
    public async Task StandaloneGzipGetsAUsefulMemberName()
    {
        string folder = TestMediaFixture.NewFolder(), source = Path.Combine(folder, "song.mp3.gz");
        using (var file = File.Create(source))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal)) gzip.Write("music"u8);
        var result = await Run(source);
        Assert.True(result.Succeeded == 1, string.Join(";", result.Files.Select(f => f.Message)));
        using var zip = ZipFile.OpenRead(Path.Combine(folder, "song.mp3.zip"));
        using var reader = new StreamReader(zip.GetEntry("song.mp3")!.Open());
        Assert.Equal("music", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task RelativeTarNamesAndLevelZeroAreSupported()
    {
        string folder = TestMediaFixture.NewFolder(), source = Seed(folder, name: "./notes.txt");
        var result = await new SelectionConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(new(Action.Archive_To_Zip, [source], new() { DeleteOldFiles = false, ZipCompression = ZipCompression.NoCompression }, OverwritePolicy.Fail));
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(f => f.Message)));
        using var zip = ZipFile.OpenRead(Path.Combine(folder, "source.zip"));
        Assert.NotNull(zip.GetEntry("notes.txt"));
        Assert.Equal(0, BitConverter.ToUInt16(File.ReadAllBytes(Path.Combine(folder, "source.zip")), 8));
    }
    [Fact]
    public async Task DuplicateArchivePathsFailWithoutPublishing()
    {
        string folder = TestMediaFixture.NewFolder(), source = Seed(folder, name: "zero.txt");
        var result = await Run(source, delete: true);
        Assert.Equal(1, result.Failed);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(folder, "source.zip")));
    }
}

