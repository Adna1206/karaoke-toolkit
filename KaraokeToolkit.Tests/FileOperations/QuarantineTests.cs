using KaraokeToolkit.Core.Execution;
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

namespace KaraokeToolkit.Tests.FileOperations;

public class QuarantineTests
{

    [Fact]
    public async Task MovePreservesRelativePathsAndCanBeRestoredTwice()
    {
        string root = TestMediaFixture.NewFolder(), folder = Path.Combine(root, "Disc 1");
        Directory.CreateDirectory(folder);
        TestMediaFixture.Seed(folder, Action.AudioGToBin, "short-graphics");
        var before = Directory.GetFiles(folder).ToDictionary(p => Path.GetFileName(p), FileTransaction.Hash);
        var finding = Assert.Single((await new ValidationService().ScanAsync([root], true)).Findings);
        var moved = Assert.Single(await new QuarantineService().MoveAsync([finding]));
        Assert.True(moved.Succeeded, moved.Message);
        Assert.Equal(2, moved.MovedFiles.Count);
        Assert.All(moved.MovedFiles, p => Assert.Contains(Path.Combine("files", "Disc 1"), p));
        Assert.Empty(Directory.GetFiles(folder));
        await new QuarantineService().RestoreAsync(moved.Manifest!);
        await new QuarantineService().RestoreAsync(moved.Manifest!);
        Assert.All(Directory.GetFiles(folder), p => Assert.Equal(before[Path.GetFileName(p)], FileTransaction.Hash(p)));
    }
    [Fact]
    public async Task ChangedSinceScanIsNeverMoved()
    {
        string folder = TestMediaFixture.NewFolder();
        File.WriteAllText(Path.Combine(folder, "song.cdg"), "bad");
        var finding = Assert.Single((await new ValidationService().ScanAsync([folder], true)).Findings);
        File.WriteAllText(finding.Source, "changed");
        var result = Assert.Single(await new QuarantineService().MoveAsync([finding]));
        Assert.False(result.Succeeded);
        Assert.Equal("changed", File.ReadAllText(finding.Source));
    }
    [Fact]
    public async Task RestoreWillNotOverwriteAReplacement()
    {
        string folder = TestMediaFixture.NewFolder();
        File.WriteAllText(Path.Combine(folder, "song.cdg"), "bad");
        var finding = Assert.Single((await new ValidationService().ScanAsync([folder], true)).Findings);
        var move = Assert.Single(await new QuarantineService().MoveAsync([finding]));
        File.WriteAllText(finding.Source, "replacement");
        await Assert.ThrowsAsync<IOException>(() => new QuarantineService().RestoreAsync(move.Manifest!));
        Assert.Equal("replacement", File.ReadAllText(finding.Source));
        Assert.True(File.Exists(move.MovedFiles[0]));
    }
    [Fact]
    public async Task ValidFilesCannotBeQuarantinedFromValidation()
    {
        string folder = ZipFolder();
        var finding = Assert.Single((await new ValidationService().ScanAsync([folder], false)).Findings);
        Assert.False(Assert.Single(await new QuarantineService().MoveAsync([finding])).Succeeded);
        Assert.True(File.Exists(finding.Source));
    }
    [Fact]
    public async Task TraversalInDamagedManifestIsRejected()
    {
        string root = TestMediaFixture.NewFolder(), directory = Path.Combine(root, "Damaged", "batch");
        Directory.CreateDirectory(directory);
        string manifest = Path.Combine(directory, "manifest.json");
        DurableJson.Write(manifest, new QuarantineManifest { Entries = [new("../outside.cdg", new string('0', 64))] });
        await Assert.ThrowsAsync<IOException>(() => new QuarantineService().RestoreAsync(manifest));
    }
    [Fact]
    public async Task FailedPairMoveRollsBackTheMemberAlreadyMoved()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioGToBin, "short-graphics");
        var finding = Assert.Single((await new ValidationService().ScanAsync([folder], false)).Findings);
        using (var locked = new FileStream(Path.Combine(folder, "song.wav"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = Assert.Single(await new QuarantineService().MoveAsync([finding]));
            Assert.False(result.Succeeded);
            Assert.All(finding.Files, f => Assert.Equal(f.Sha256, FileTransaction.Hash(f.Path)));
        }
    }
    [Fact]
    public async Task InterruptedDamagedMoveCanBeRestoredFromManifest()
    {
        string root = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(root, Action.AudioGToBin, "short-graphics");
        var finding = Assert.Single((await new ValidationService().ScanAsync([root], false)).Findings);
        string batch = Path.Combine(root, "Damaged", "interrupted"), files = Path.Combine(batch, "files");
        Directory.CreateDirectory(files);
        string manifest = Path.Combine(batch, "manifest.json");
        DurableJson.Write(manifest, new QuarantineManifest { Entries = finding.Files.Select(f => new QuarantineEntry(Path.GetFileName(f.Path), f.Sha256)).ToArray() });
        File.Move(finding.Files[0].Path, Path.Combine(files, Path.GetFileName(finding.Files[0].Path)));
        await new QuarantineService().RestoreAsync(manifest);
        Assert.All(finding.Files, f => Assert.Equal(f.Sha256, FileTransaction.Hash(f.Path)));
    }
}
