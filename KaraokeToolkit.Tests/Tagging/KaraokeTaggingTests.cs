using System.Diagnostics;
using System.IO.Compression;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Tagging;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Tests.Tagging;

public class KaraokeTaggingTests
{
    [Theory]
    [InlineData(".mp3", "00")]
    [InlineData(".wav", "0000")]
    [InlineData(".mkv", "0")]
    public async Task ZeroTrackCanReplaceAnExistingTrackWithoutFailingVerification(string extension, string track)
    {
        string path = await CreateMedia(extension);
        string renamed = path.Replace("SF123-04", "DISC-" + track, StringComparison.Ordinal);
        File.Move(path, renamed);
        if (extension != ".mkv")
        {
            using var media = TagLib.File.Create(renamed);
            media.Tag.Track = 8; media.Save();
        }
        var service = new KaraokeTaggingService();
        var preview = await service.PreviewAsync([renamed], FilenameTagPattern.Default, false);
        Assert.True(preview[0].CanApply, preview[0].Error);
        Assert.Equal(0u, preview[0].Tags!.Track);
        var result = await service.ApplyAsync(preview, new(), new());
        Assert.True(result.Succeeded == 1, result.Files[0].Message);
        Assert.Equal(0u, KaraokeTaggingService.ReadTags(renamed).Track ?? 0);
    }

    internal static async Task<string> CreateMedia(string extension = ".mp3")
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        string path = Path.Combine(folder, "SF123-04 - Artist - Title" + extension);
        if (extension == ".wav") File.Move(Path.Combine(folder, "song.wav"), path);
        else
        {
            var start = new ProcessStartInfo(Path.Combine(FFmpegRuntime.Options(folder).BinaryFolder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            var arguments = new List<string> { "-nostdin", "-v", "error", "-i", Path.Combine(folder, "song.wav") };
            if (extension is ".mp4" or ".mkv" or ".avi" or ".webm")
            {
                arguments.AddRange(["-f", "lavfi", "-i", "color=c=blue:s=160x120:r=10", "-shortest", "-c:v", extension == ".webm" ? "libvpx-vp9" : "mpeg4"]);
            }
            if (extension == ".m4a") arguments.AddRange(["-c:a", "alac"]);
            arguments.Add(path);
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            string error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, error);
            File.Delete(Path.Combine(folder, "song.wav"));
        }
        File.Move(Path.Combine(folder, "song.cdg"), Path.ChangeExtension(path, ".cdg"));
        return path;
    }

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".ogg")]
    [InlineData(".opus")]
    [InlineData(".wav")]
    [InlineData(".flac")]
    [InlineData(".wma")]
    [InlineData(".aiff")]
    [InlineData(".m4a")]
    [InlineData(".wv")]
    [InlineData(".mp4")]
    [InlineData(".mkv")]
    [InlineData(".avi")]
    [InlineData(".webm")]
    public async Task WritesFourTagsWithoutReencodingAndKeepsGraphics(string extension)
    {
        string path = await CreateMedia(extension), cdg = Path.ChangeExtension(path, ".cdg");
        string graphicsHash = FileTransaction.Hash(cdg);
        string pcm = Path.Combine(TestMediaFixture.NewFolder(), "before.pcm");
        await AudioDecoderService.DecodeAsync(path, pcm, default, resampleForMp3G: true);
        string audioHash = FileTransaction.Hash(pcm);
        var service = new KaraokeTaggingService();
        var preview = await service.PreviewAsync([path, cdg], FilenameTagPattern.Default, false);
        Assert.True(Assert.Single(preview).CanApply, preview[0].Error);
        var result = await service.ApplyAsync(preview, new(), new());
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(f => f.Message)));
        Assert.Equal(new KaraokeTags("SF123", 4, "Artist", "Title"), KaraokeTaggingService.ReadTags(path));
        await AudioDecoderService.DecodeAsync(path, Path.Combine(Path.GetDirectoryName(pcm)!, "after.pcm"), default, resampleForMp3G: true);
        Assert.Equal(audioHash, FileTransaction.Hash(Path.Combine(Path.GetDirectoryName(pcm)!, "after.pcm")));
        Assert.Equal(graphicsHash, FileTransaction.Hash(cdg));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(path)!, ".karaoke-job-*"));
    }

    [Fact]
    public async Task ClearsDescriptiveJunkButRetainsArtworkAndFillEmptyFields()
    {
        string path = await CreateMedia();
        byte[] artwork = [1, 2, 3, 4];
        using (var media = TagLib.File.Create(path))
        {
            media.Tag.Title = "Keep title"; media.Tag.Album = "Keep album"; media.Tag.Comment = "Old comment";
            media.Tag.Genres = ["Old genre"]; media.Tag.Year = 1999; media.Tag.Disc = 9;
            media.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(artwork)) { MimeType = "image/jpeg", Type = TagLib.PictureType.FrontCover }];
            media.Save();
        }
        var service = new KaraokeTaggingService();
        var preview = await service.PreviewAsync([path], FilenameTagPattern.Default, false);
        var result = await service.ApplyAsync(preview, new(FillEmptyOnly: true), new());
        Assert.True(result.Succeeded == 1, result.Files[0].Message);
        using var tagged = TagLib.File.Create(path);
        Assert.Equal("Keep title", tagged.Tag.Title); Assert.Equal("Keep album", tagged.Tag.Album);
        Assert.Equal("Artist", tagged.Tag.FirstPerformer); Assert.Equal(4u, tagged.Tag.Track);
        Assert.True(string.IsNullOrEmpty(tagged.Tag.Comment)); Assert.Empty(tagged.Tag.Genres);
        Assert.Equal(0u, tagged.Tag.Year); Assert.Equal(0u, tagged.Tag.Disc);
        Assert.Equal(artwork, Assert.Single(tagged.Tag.Pictures).Data.Data);
    }

    [Fact]
    public async Task ZipUsesExternalNamePreservesMemberNamesGraphicsAndUpdatesOnlyArchive()
    {
        string audio = await CreateMedia(), folder = Path.GetDirectoryName(audio)!;
        string zipPath = Path.ChangeExtension(audio, ".zip"), cdg = Path.ChangeExtension(audio, ".cdg");
        byte[] graphics = File.ReadAllBytes(cdg);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        { zip.CreateEntryFromFile(audio, "nested/generic.mp3"); zip.CreateEntryFromFile(cdg, "nested/generic.cdg"); }
        string audioHash = FileTransaction.Hash(audio);
        var service = new KaraokeTaggingService();
        var preview = await service.PreviewAsync([zipPath], FilenameTagPattern.Default, false);
        Assert.True(preview[0].CanApply, preview[0].Error);
        var result = await service.ApplyAsync(preview, new(), new() { DeleteOldFiles = false });
        Assert.True(result.Succeeded == 1, result.Files[0].Message);
        using var taggedZip = ZipFile.OpenRead(zipPath);
        Assert.Equal(new[] { "nested/generic.cdg", "nested/generic.mp3" }, taggedZip.Entries.Select(e => e.FullName).Order().ToArray());
        using var stream = taggedZip.GetEntry("nested/generic.cdg")!.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
        Assert.Equal(graphics, memory.ToArray());
        string extracted = Path.Combine(TestMediaFixture.NewFolder(), "generic.mp3");
        taggedZip.GetEntry("nested/generic.mp3")!.ExtractToFile(extracted);
        Assert.Equal(new KaraokeTags("SF123", 4, "Artist", "Title"), KaraokeTaggingService.ReadTags(extracted));
        Assert.Equal(audioHash, FileTransaction.Hash(audio));
        Assert.Equal(graphics, File.ReadAllBytes(cdg)); Assert.Equal(3, Directory.GetFiles(folder).Length);
    }

    [Fact]
    public async Task ChangedCorruptUnsupportedAndCancelledInputsKeepOriginals()
    {
        string path = await CreateMedia(); var service = new KaraokeTaggingService();
        var preview = await service.PreviewAsync([path], FilenameTagPattern.Default, false);
        using (var media = TagLib.File.Create(path)) { media.Tag.Title = "Changed"; media.Save(); }
        string hash = FileTransaction.Hash(path);
        var result = await service.ApplyAsync(preview, new(), new());
        Assert.Equal(1, result.Failed); Assert.Equal(hash, FileTransaction.Hash(path));
        var cancelled = await service.ApplyAsync(await service.PreviewAsync([path], FilenameTagPattern.Default, false), new(), new(), token: new CancellationToken(true));
        Assert.True(cancelled.Cancelled); Assert.Equal(hash, FileTransaction.Hash(path));
        string bin = Path.ChangeExtension(path, ".bin"); File.WriteAllBytes(bin, [1, 2]);
        string broken = Path.Combine(Path.GetDirectoryName(path)!, "SF124-01 - Artist - Broken.mp3"); File.WriteAllBytes(broken, [1, 2]);
        var invalid = await service.PreviewAsync([bin, broken], FilenameTagPattern.Default, false);
        Assert.All(invalid, item => Assert.False(item.CanApply));
        Assert.Equal(2, (await service.ApplyAsync(invalid, new(), new())).Skipped);
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(bin));
    }

    [Fact]
    public async Task RecursionDeduplicatesOverlappingSelectionsAndSkipsProtectedFolders()
    {
        string path = await CreateMedia(".wav"), root = Path.GetDirectoryName(path)!;
        string child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
        string second = Path.Combine(child, Path.GetFileName(path)); File.Copy(path, second);
        string damaged = Directory.CreateDirectory(Path.Combine(root, "Damaged")).FullName; File.Copy(path, Path.Combine(damaged, Path.GetFileName(path)));
        var service = new KaraokeTaggingService();
        Assert.Single(await service.PreviewAsync([root, path], FilenameTagPattern.Default, false));
        Assert.Equal(2, (await service.PreviewAsync([root, child, path], FilenameTagPattern.Default, true)).Count);
    }

    [Fact]
    public async Task CanRetainCommentsAndDescriptiveTagsAndPreservesFieldsMissingFromPattern()
    {
        string path = await CreateMedia();
        using (var media = TagLib.File.Create(path))
        {
            media.Tag.Title = "Old title"; media.Tag.Album = "Existing disc"; media.Tag.Track = 8;
            media.Tag.Performers = ["Singer 1", "Singer 2"]; media.Tag.Comment = "Keep comment"; media.Tag.Year = 1998;
            media.Save();
        }
        var service = new KaraokeTaggingService();
        var preview = await service.PreviewAsync([path], "%TITLE%", false);
        var result = await service.ApplyAsync(preview, new(ClearComments: false, ClearOtherDescriptiveTags: false), new());
        Assert.True(result.Succeeded == 1, result.Files[0].Message);
        using var tagged = TagLib.File.Create(path);
        Assert.Equal("Existing disc", tagged.Tag.Album); Assert.Equal(8u, tagged.Tag.Track);
        Assert.Equal(new[] { "Singer 1", "Singer 2" }, tagged.Tag.Performers);
        Assert.Equal("Keep comment", tagged.Tag.Comment); Assert.Equal(1998u, tagged.Tag.Year);
    }

    [Fact]
    public async Task ReadOnlyMediaAndInvalidZipCannotReplaceOriginals()
    {
        string path = await CreateMedia(".wav");
        var service = new KaraokeTaggingService(); string hash = FileTransaction.Hash(path);
        var preview = await service.PreviewAsync([path], FilenameTagPattern.Default, false);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try
        {
            var result = await service.ApplyAsync(preview, new(), new());
            Assert.Equal(1, result.Failed); Assert.Equal(hash, FileTransaction.Hash(path));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        string zip = Path.ChangeExtension(path, ".zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        { archive.CreateEntryFromFile(path, "../escape.wav"); archive.CreateEntryFromFile(Path.ChangeExtension(path, ".cdg"), "escape.cdg"); }
        string zipHash = FileTransaction.Hash(zip);
        var invalid = await service.PreviewAsync([zip], FilenameTagPattern.Default, false);
        Assert.False(invalid[0].CanApply); Assert.Contains("unsafe", invalid[0].Error);
        Assert.Equal(1, (await service.ApplyAsync(invalid, new(), new())).Skipped); Assert.Equal(zipHash, FileTransaction.Hash(zip));
    }

    [Fact]
    public async Task RecoveryFinishesInterruptedPublicationBeforeNewPreview()
    {
        string original = await CreateMedia(".wav"), folder = Path.GetDirectoryName(original)!;
        string workspace = Directory.CreateDirectory(Path.Combine(folder, ".karaoke-job-test-tagging")).FullName;
        string staged = Path.Combine(workspace, Path.GetFileName(original)); File.Copy(original, staged);
        using (var media = TagLib.File.Create(staged)) { media.Tag.Title = "Recovered"; media.Save(); }
        string oldHash = FileTransaction.Hash(original), newHash = FileTransaction.Hash(staged);
        Directory.CreateDirectory(Path.Combine(workspace, "backups"));
        File.Move(original, Path.Combine(workspace, "backups", "0"));
        DurableJson.Write(Path.Combine(workspace, "tagging.json"), new { Version = 1 });
        DurableJson.Write(Path.Combine(workspace, "recovery.json"), new TransactionJournal
        { Outputs = [new(Path.GetFileName(original), newHash)], Backups = [new(Path.GetFileName(original), "0", oldHash)] });
        var service = new KaraokeTaggingService();
        Assert.Equal(1, await service.RecoverInterruptedPublicationsAsync([folder], false));
        Assert.Equal(newHash, FileTransaction.Hash(original)); Assert.False(Directory.Exists(workspace));
        Assert.Equal("Recovered", (await service.PreviewAsync([original], FilenameTagPattern.Default, false))[0].Existing!.Title);
    }

    [Fact]
    public async Task CancellationAfterFirstFileKeepsCompletedTagsAndLeavesNextFileUntouched()
    {
        string first = await CreateMedia(".wav"), second = await CreateMedia(".wav");
        string secondHash = FileTransaction.Hash(second);
        var service = new KaraokeTaggingService();
        var firstPreview = Assert.Single(await service.PreviewAsync([first], FilenameTagPattern.Default, false));
        var secondPreview = Assert.Single(await service.PreviewAsync([second], FilenameTagPattern.Default, false));
        using var cancellation = new CancellationTokenSource();
        var result = await service.ApplyAsync([firstPreview, secondPreview], new(), new(),
            new SynchronousProgress<ConversionProgress>(_ => cancellation.Cancel()), cancellation.Token);
        Assert.True(result.Cancelled); Assert.Equal(1, result.Succeeded);
        Assert.Equal("Title", KaraokeTaggingService.ReadTags(first).Title);
        Assert.Equal(secondHash, FileTransaction.Hash(second));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(first)!, ".karaoke-job-*"));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(second)!, ".karaoke-job-*"));
    }
}
