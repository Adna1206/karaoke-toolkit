using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.App.Features.Settings;
using System.Text.Json;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core.Validation;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;
using DotNetZip = System.IO.Compression.ZipFile;
namespace KaraokeToolkit.Tests.Archives;

public class ManagedZipTests
{
    [Theory]
    [InlineData(ZipCompression.NoCompression, 0)]
    [InlineData(ZipCompression.Fast, 8)]
    [InlineData(ZipCompression.Level2, 8)]
    [InlineData(ZipCompression.Level3, 8)]
    [InlineData(ZipCompression.Level4, 8)]
    [InlineData(ZipCompression.Level6, 8)]
    [InlineData(ZipCompression.Level7, 8)]
    [InlineData(ZipCompression.Level8, 8)]
    [InlineData(ZipCompression.Balanced, 8)]
    [InlineData(ZipCompression.SmallestSize, 8)]
    public async Task OutputUsesCompatibleMethodsAndPreservesUnicodePayloads(ZipCompression compression, int method)
    {
        string folder = TestMediaFixture.NewFolder();
        string audio = Path.Combine(folder, "André - Song.mp3"), cdg = Path.ChangeExtension(audio, ".cdg"), zip = Path.Combine(folder, "output.zip");
        byte[] music = Enumerable.Range(0, 65536).Select(i => (byte)i).ToArray(), graphics = new byte[240];
        File.WriteAllBytes(audio, music); File.WriteAllBytes(cdg, graphics);
        await KaraokeZipWriter.CreateAsync(audio, cdg, zip, compression, default);
        Assert.Equal(method, BitConverter.ToUInt16(File.ReadAllBytes(zip), 8));
        using var archive = DotNetZip.OpenRead(zip);
        Assert.Equal(2, archive.Entries.Count);
        foreach (var pair in new[] { (audio, music), (cdg, graphics) })
        {
            using var entry = archive.GetEntry(Path.GetFileName(pair.Item1))!.Open();
            using var output = new MemoryStream(); await entry.CopyToAsync(output);
            Assert.Equal(pair.Item2, output.ToArray());
        }
    }

    [Theory]
    [InlineData(CompressionType.BZip2)]
    [InlineData(CompressionType.LZMA)]
    public async Task NonDeflateArchivesValidateAndExtract(CompressionType type)
    {
        string folder = TestMediaFixture.NewFolder(), zip = Path.Combine(folder, "song.zip");
        byte[] music = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray(), graphics = new byte[240];
        using (var stream = File.Create(zip))
        using (var writer = new ZipWriter(stream, new ZipWriterOptions(type)))
        {
            foreach (var pair in new[] { ("song.mp3", music), ("song.cdg", graphics) })
            {
                using var entry = writer.WriteToStream(pair.Item1, new ZipWriterEntryOptions());
                entry.Write(pair.Item2);
            }
        }
        var result = await new ValidationService().ScanAsync([zip], false);
        Assert.Equal(ValidationStatus.Valid, Assert.Single(result.Findings).Status);
        var extracted = await KaraokeArchive.ExtractAsync(zip, default);
        Assert.Equal(music, File.ReadAllBytes(extracted.Audio));
        Assert.Equal(graphics, File.ReadAllBytes(extracted.Graphics));
    }

    [Theory]
    [InlineData(0, ZipCompression.NoCompression)]
    [InlineData(1, ZipCompression.Fast)]
    [InlineData(2, ZipCompression.Level2)]
    [InlineData(3, ZipCompression.Level3)]
    [InlineData(4, ZipCompression.Level4)]
    [InlineData(5, ZipCompression.Balanced)]
    [InlineData(6, ZipCompression.Level6)]
    [InlineData(7, ZipCompression.Level7)]
    [InlineData(8, ZipCompression.Level8)]
    [InlineData(9, ZipCompression.SmallestSize)]
    public void LegacySettingsAndRecoveryMigrate(int level, ZipCompression expected)
    {
        var options = JsonSerializer.Deserialize<ConversionOptions>("{\"ZipCompressionLevel\":" + level + "}")!;
        Assert.Equal(expected, options.ZipCompression);
        Assert.Equal(options, JsonSerializer.Deserialize<ConversionOptions>(JsonSerializer.Serialize(options)));
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xmls");
        File.WriteAllText(path, $"<profile><section name='Options'><entry name='ZipCompressionLevel'>{level}</entry></section></profile>");
        var store = new SettingsStore(path);
        Assert.Equal(expected, store.Load().ZipCompression);
        store.Save(options);
        Assert.Equal(expected, store.Load().ZipCompression);
    }

    [Fact]
    public async Task CancelledWriterDoesNotCreateOutput()
    {
        string folder = TestMediaFixture.NewFolder(), zip = Path.Combine(folder, "output.zip");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => KaraokeZipWriter.CreateAsync("unused.mp3", "unused.cdg", zip, ZipCompression.Balanced, new CancellationToken(true)));
        Assert.False(File.Exists(zip));
    }

    [Fact]
    public async Task InvalidArchiveIsReportedWithoutAbortingScan()
    {
        string zip = Path.Combine(TestMediaFixture.NewFolder(), "broken.zip");
        File.WriteAllText(zip, "not an archive");
        var result = await new ValidationService().ScanAsync([zip], false);
        Assert.Equal(ValidationStatus.Damaged, Assert.Single(result.Findings).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptLocalHeaderOrEncryptionFlagsAreNotAccepted(bool encrypted)
    {
        string folder = TestMediaFixture.NewFolder();
        string audio = Path.Combine(folder, "song.mp3"), cdg = Path.Combine(folder, "song.cdg"), zip = Path.Combine(folder, "song.zip");
        File.WriteAllBytes(audio, new byte[1024]); File.WriteAllBytes(cdg, new byte[240]);
        await KaraokeZipWriter.CreateAsync(audio, cdg, zip, ZipCompression.Balanced, default);
        byte[] bytes = File.ReadAllBytes(zip);
        if (encrypted)
        {
            bytes[6] |= 1;
            int central = Enumerable.Range(0, bytes.Length - 4).First(i => BitConverter.ToUInt32(bytes, i) == 0x02014b50);
            bytes[central + 8] |= 1;
        }
        else bytes[14] ^= 1;
        File.WriteAllBytes(zip, bytes);
        var result = await new ValidationService().ScanAsync([zip], false);
        Assert.Equal(encrypted ? ValidationStatus.Unsupported : ValidationStatus.Damaged, Assert.Single(result.Findings).Status);
    }

    [Theory]
    [InlineData("NoCompression", 0)]
    [InlineData("Fast", 1)]
    [InlineData("Balanced", 6)]
    [InlineData("SmallestSize", 9)]
    public void FormerPresetsKeepTheirActualLevels(string preset, int level)
    {
        var options = JsonSerializer.Deserialize<ConversionOptions>("{\"ZipCompression\":\"" + preset + "\"}")!;
        Assert.Equal(level, (int)options.ZipCompression);
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xmls");
        File.WriteAllText(path, $"<profile><KaraokeToolkit><ZipCompression>{preset}</ZipCompression></KaraokeToolkit></profile>");
        Assert.Equal(level, (int)new SettingsStore(path).Load().ZipCompression);
    }

    [Theory]
    [InlineData(ZipAlgorithm.Deflate, 8)]
    [InlineData(ZipAlgorithm.BZip2, 12)]
    [InlineData(ZipAlgorithm.Lzma, 14)]
    public async Task SelectedAlgorithmPersistsAndReachesConversion(ZipAlgorithm algorithm, int method)
    {
        string folder = TestMediaFixture.NewFolder();
        var options = new ConversionOptions { ZipAlgorithm = algorithm, DeleteOldFiles = false, AutoRenameZip = false, AutoCreateCueText = false };
        var vm = new KaraokeToolkit.App.Features.Settings.OptionsViewModel(options);
        Assert.Equal(algorithm == ZipAlgorithm.Deflate, vm.IsDeflate);
        Assert.Equal(options, vm.ToOptions());
        string settings = Path.Combine(folder, "settings.xmls");
        var store = new SettingsStore(settings);
        store.Save(options);
        Assert.Equal(options, store.Load());
        Assert.Equal(options, JsonSerializer.Deserialize<ConversionOptions>(JsonSerializer.Serialize(options)));
        TestMediaFixture.Seed(folder, OperationCatalog.defAction.AudioG_To_Bin, "keep");
        var result = await new ConversionService(new KaraokeToolkit.Core.Recovery.RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(new(OperationCatalog.defAction.AudioG_To_Zip, folder, options));
        string zip = Path.Combine(folder, "song.zip");
        Assert.True(File.Exists(zip), JsonSerializer.Serialize(result));
        Assert.Equal(method, BitConverter.ToUInt16(File.ReadAllBytes(zip), 8));
        Assert.Equal(ValidationStatus.Valid, Assert.Single((await new ValidationService().ScanAsync([zip], false)).Findings).Status);
    }

    [Fact]
    public void MissingAlgorithmDefaultsToDeflateAndInvalidAlgorithmIsRejected()
    {
        Assert.Equal(ZipAlgorithm.Deflate, JsonSerializer.Deserialize<ConversionOptions>("{}")!.ZipAlgorithm);
        var options = new ConversionOptions { ZipAlgorithm = (ZipAlgorithm)99 };
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationRequest(OperationCatalog.defAction.AudioG_To_Zip, ".", options).Validate());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConversionOptions>("{\"ZipAlgorithm\":99}"));
    }
}
