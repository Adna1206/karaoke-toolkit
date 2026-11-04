using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Settings;
using static KaraokeToolkit.Tests.Support.FileWorkflowFixture;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Validation;
using System.IO.Compression;
using System.Text.Json;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.Validation;

public class ValidationTests
{

    [Fact]
    public async Task ValidationIsReadOnlyAndDeduplicatesPairs()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioGToBin, "keep");
        var before = TestMediaFixture.Snapshot(folder);
        var result = await new ValidationService().ScanAsync([folder], true);
        Assert.Equal(ValidationStatus.Valid, Assert.Single(result.Findings).Status);
        Assert.Equal(before, TestMediaFixture.Snapshot(folder));
    }
    [Theory]
    [InlineData("missing-audio", ValidationStatus.Incomplete)]
    [InlineData("missing-graphics", ValidationStatus.Incomplete)]
    [InlineData("short-graphics", ValidationStatus.Damaged)]
    public async Task PairProblemsHaveDifferentStatuses(string mode, ValidationStatus expected)
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioGToBin, mode);
        var finding = Assert.Single((await new ValidationService().ScanAsync([folder], true)).Findings);
        Assert.Equal(expected, finding.Status);
        Assert.True(finding.CanMove);
    }
    [Fact]
    public async Task MultipleAudioMatchesAreAmbiguous()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioGToBin, "keep");
        File.WriteAllText(Path.Combine(folder, "song.mp3"), "another candidate");
        var result = await new ValidationService().ScanAsync([Path.Combine(folder, "song.cdg")], false);
        Assert.Equal(ValidationStatus.Ambiguous, Assert.Single(result.Findings).Status);
        Assert.Equal(3, result.Findings[0].Files.Count);
    }
    [Theory]
    [InlineData(false, ValidationStatus.Valid)]
    [InlineData(true, ValidationStatus.Damaged)]
    public async Task BinStructureIsChecked(bool truncated, ValidationStatus expected)
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.BinToAudioG, truncated ? "truncated-bin" : "keep");
        Assert.Equal(expected, Assert.Single((await new ValidationService().ScanAsync([folder], false)).Findings).Status);
    }
    [Fact]
    public async Task ZipValidationReadsCrcsAndDoesNotExtract()
    {
        string folder = ZipFolder();
        var before = TestMediaFixture.Snapshot(folder);
        Assert.Equal(ValidationStatus.Valid, Assert.Single((await new ValidationService().ScanAsync([folder], false)).Findings).Status);
        Assert.Equal(before, TestMediaFixture.Snapshot(folder));
        string zip = Path.Combine(folder, "song.zip");
        byte[] data = File.ReadAllBytes(zip);
        // Alter the central-directory CRC without changing the compressed payload.
        int central = Enumerable.Range(0, data.Length - 4).First(i => BitConverter.ToUInt32(data, i) == 0x02014b50);
        data[central + 16] ^= 1;
        File.WriteAllBytes(zip, data);
        Assert.Equal(ValidationStatus.Damaged, Assert.Single((await new ValidationService().ScanAsync([folder], false)).Findings).Status);
    }
    [Fact]
    public async Task ReadableZipWithExtraFilesIsUnsupportedRatherThanCorrupt()
    {
        string folder = ZipFolder();
        using (var zip = ZipFile.Open(Path.Combine(folder, "song.zip"), ZipArchiveMode.Update))
        using (var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open()))
            writer.Write("notes");
        var finding = Assert.Single((await new ValidationService().ScanAsync([folder], true)).Findings);
        Assert.Equal(ValidationStatus.Unsupported, finding.Status);
    }
    [Fact]
    public async Task DamagedAndRecoveryFoldersAreExcludedFromScansAndConversion()
    {
        string root = TestMediaFixture.NewFolder();
        foreach (string name in new[] { "Damaged", ".karaoke-job-stale" })
        {
            string child = Path.Combine(root, name);
            Directory.CreateDirectory(child);
            File.WriteAllText(Path.Combine(child, "bad.zip"), "broken");
        }
        Assert.Empty((await new ValidationService().ScanAsync([root], true)).Findings);
        Assert.Empty(InputSelection.Plan(Action.ZipToAudioG, [root], true));
        Assert.Throws<IOException>(() => InputSelection.Normalize([Path.Combine(root, "Damaged")]));
    }
}
