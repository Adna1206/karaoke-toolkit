using System.Diagnostics;
using System.Text.Json;
using KaraokeToolkit.Cli;
using KaraokeToolkit.Core;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Tagging;
using KaraokeToolkit.Core.Video;

namespace KaraokeToolkit.Tests.Cli;

public class CliTests
{
    private sealed class FakeExecutor : IOperationExecutor
    {
        public OperationRequest? Request;
        public ExecutionResult Result = new(new("Test", DateTimeOffset.Now, false, []), 1, false);
        public bool Cancel;
        public Task<ExecutionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken token)
        {
            Request = request; progress?.Report(new(50, 50, 1, 0, "Working", "00:00:00"));
            if (Cancel) throw new OperationCanceledException(token);
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public async Task ArgumentsMapToFrozenSettingsAndProgressStaysOffJsonOutput()
    {
        var executor = new FakeExecutor(); var output = new StringWriter(); var error = new StringWriter();
        int code = await new CliApplication(executor).RunAsync(["run", "audioG-to-zip", "folder with spaces", "--input", "another folder",
            "--encoder", "FLAC", "--bitrate", "256", "--zip-level", "9", "--zip-algorithm", "BZip2", "--delete-originals", "--no-recurse", "--no-cue-text", "--overwrite", "replace", "--json"], output, error);
        Assert.Equal(0, code); Assert.Equal(OperationId.AudioGToZip, executor.Request!.OperationId);
        Assert.Equal(new[] { "folder with spaces", "another folder" }, executor.Request.Inputs);
        Assert.Equal(AudioEncoder.FLAC, executor.Request.Options.AudioEncoder); Assert.Equal(256, executor.Request.Options.AudioBitrate);
        Assert.Equal(ZipCompression.SmallestSize, executor.Request.Options.ZipCompression); Assert.Equal(ZipAlgorithm.BZip2, executor.Request.Options.ZipAlgorithm);
        Assert.True(executor.Request.Options.DeleteOldFiles); Assert.False(executor.Request.Options.RecurseFolders); Assert.False(executor.Request.Options.AutoCreateCueText);
        Assert.Equal(OverwritePolicy.Replace, executor.Request.OverwritePolicy);
        using var json = JsonDocument.Parse(output.ToString()); Assert.Equal(0, json.RootElement.GetProperty("ExitCode").GetInt32());
        Assert.Contains("Working", error.ToString()); Assert.DoesNotContain("Working", output.ToString());
    }

    [Theory]
    [InlineData("recipe", "name")]
    [InlineData("run", "DiscRip")]
    [InlineData("run", "ZipToAudioG", "--input")]
    [InlineData("validate", "x", "--typo")]
    [InlineData("validate", "x", "--overwrite", "ask")]
    [InlineData("validate", "x", "--overwrite", "2")]
    [InlineData("validate", "x", "--json=true")]
    [InlineData("validate", "x", "--recurse", "--no-recurse")]
    [InlineData("validate", "x", "--quiet", "--quiet")]
    [InlineData("run", "audiog-to-zip", "x", "--zip-level", "10")]
    [InlineData("run", "audiog-to-zip", "x", "--encoder", "unknown")]
    [InlineData("run", "audiog-to-zip", "x", "--pattern", "%TITLE%")]
    [InlineData("validate", "x", "--video-frame-rate", "30")]
    [InlineData("run", "organize-folders", "x", "--ignore-leading-the")]
    public async Task InvalidArgumentsDoNotExecute(params string[] args)
    {
        var executor = new FakeExecutor(); var error = new StringWriter();
        Assert.Equal(2, await new CliApplication(executor).RunAsync(args, new StringWriter(), error));
        Assert.Null(executor.Request); Assert.NotEmpty(error.ToString());
    }

    [Fact]
    public void OptionsFileUsesCliSafeDefaultsAndFlagsTakePrecedence()
    {
        string file = Path.Combine(TestMediaFixture.NewFolder(), "options.json");
        File.WriteAllText(file, """{"AudioEncoder":"WMA","AudioBitrate":192,"ZipCompression":"7","RecurseFolders":false}""");
        var parsed = CliArguments.Parse(["run", "bin-to-audiog", "x", "--options", file, "--bitrate", "256", "--recurse"]);
        Assert.Equal(AudioEncoder.WMA, parsed.Request!.Options.AudioEncoder); Assert.Equal(256, parsed.Request.Options.AudioBitrate);
        Assert.False(parsed.Request.Options.DeleteOldFiles); Assert.True(parsed.Request.Options.RecurseFolders);
        Assert.Equal(ZipCompression.Level7, parsed.Request.Options.ZipCompression);
        File.WriteAllText(file, """{"DeleteOldFiles":true}""");
        Assert.False(CliArguments.Parse(["tag", "x", "--options", file, "--keep-originals"]).Request!.Options.DeleteOldFiles);
        File.WriteAllText(file, """{"Typo":true}""");
        Assert.Throws<CliUsageException>(() => CliArguments.Parse(["validate", "x", "--options", file]));
    }

    [Fact]
    public void DefaultsAndTagSettingsAreExplicitAndEndOfOptionsIsHonoured()
    {
        var parsed = CliArguments.Parse(["tag", "--pattern", "%ARTIST% - %TITLE%", "--fill-empty-only", "--preserve-comments", "--preserve-other-tags", "--", "--help"]);
        Assert.Equal("--help", Assert.Single(parsed.Inputs)); Assert.False(parsed.Request!.Options.DeleteOldFiles);
        Assert.Equal(OverwritePolicy.Fail, parsed.Request.OverwritePolicy); Assert.True(parsed.Request.Tagging.FillEmptyOnly);
        Assert.False(parsed.Request.Tagging.ClearComments); Assert.False(parsed.Request.Tagging.ClearOtherDescriptiveTags);
        Assert.Equal("%ARTIST% - %TITLE%", parsed.Request.TaggingPattern);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("\"Balanced\"", 5)]
    [InlineData("\"9\"", 9)]
    public void JsonZipLevelsUseCurrentValues(string value, int expected)
    {
        string file = Path.Combine(TestMediaFixture.NewFolder(), "options.json");
        File.WriteAllText(file, "{\"ZipCompression\":" + value + "}");
        Assert.Equal(expected, (int)CliArguments.Parse(["run", "audiog-to-zip", "x", "--options", file]).Request!.Options.ZipCompression);
    }

    [Theory]
    [InlineData("AudioEncoder", "0")]
    [InlineData("ZipAlgorithm", "1")]
    [InlineData("VideoContainer", "\"0\"")]
    [InlineData("AudioBitrate", "128,\"AudioBitrate\":192")]
    public void OptionsFilesRejectNumericEnumsAndRepeatedSettings(string name, string value)
    {
        string file = Path.Combine(TestMediaFixture.NewFolder(), "options.json");
        File.WriteAllText(file, "{\"" + name + "\":" + value + "}");
        Assert.Throws<CliUsageException>(() => CliArguments.Parse(["run", "karaoke-to-video", "x", "--options", file]));
    }

    [Fact]
    public void VideoAndOrganizationFlagsReachTheirOwnRequests()
    {
        var video = CliArguments.Parse(["run", "karaoke-to-video", "x", "--video-container", "mkv", "--video-resolution", "FullHD",
            "--video-frame-rate", "60", "--video-bitrate", "4000", "--video-smoothing", "Bicubic",
            "--video-audio-bitrate", "256", "--video-audio-fallback-only"]);
        Assert.Equal(VideoContainer.MKV, video.Request!.Options.VideoContainer);
        Assert.Equal(VideoResolution.FullHD, video.Request.Options.VideoResolution);
        Assert.Equal(VideoGraphicsSmoothing.Bicubic, video.Request.Options.VideoGraphicsSmoothing);
        Assert.Equal(60, video.Request.Options.VideoFrameRate);
        Assert.Equal(4000, video.Request.Options.VideoBitrateKbps);
        Assert.Equal(256, video.Request.Options.VideoAudioBitrateKbps);
        Assert.False(video.Request.Options.VideoAudioMatchSourceBitrate);
        Assert.True(CliArguments.Parse(["run", "organize-files", "x", "--ignore-leading-the"]).Request!.IgnoreLeadingThe);
    }

    [Fact]
    public async Task DiscoveryShowsEachCommandAndIdentityWithoutExecuting()
    {
        var executor = new FakeExecutor(); var output = new StringWriter(); var error = new StringWriter();
        var cli = new CliApplication(executor);
        Assert.Equal(0, await cli.RunAsync(["operations"], output, error));
        foreach (var registration in CliOperationCatalog.Exposed)
        {
            Assert.Contains(registration.Name!, output.ToString());
            Assert.Contains(registration.OperationId.ToString(), output.ToString());
        }
        Assert.Contains("folders only", output.ToString());
        Assert.Null(executor.Request); Assert.Empty(error.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await cli.RunAsync(["--help"], output, error));
        Assert.Contains("Examples:", output.ToString());
        Assert.Contains("192 kbps fallback", output.ToString());
        Assert.Null(executor.Request);
    }

    [Fact]
    public async Task RecoveryListsAndResumesSavedConversionWithoutRecipes()
    {
        string folder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(folder, OperationId.ZipToBin, "keep");
        var store = new RecoveryStore(TestMediaFixture.NewFolder()); string journal;
        using (var run = new RunSession(store, new(OperationId.ZipToAudioG, folder,
            new() { AutoCreateCueText = false, DeleteOldFiles = false }), OverwritePolicy.Fail, [Path.Combine(folder, "song.zip")])) journal = run.Journal;
        var cli = new CliApplication(new OperationExecutionService(store)); var output = new StringWriter();
        Assert.Equal(0, await cli.RunAsync(["recovery-list", folder, "--json"], output, new StringWriter()));
        using var list = JsonDocument.Parse(output.ToString()); Assert.Single(list.RootElement.EnumerateArray());
        Assert.Equal(journal, list.RootElement[0].GetProperty("JournalPath").GetString());
        Assert.Equal(0, await cli.RunAsync(["recover", journal, "--quiet"], new StringWriter(), new StringWriter()));
        Assert.Empty(store.Find()); Assert.True(File.Exists(Path.Combine(folder, "song.wav")));
        Assert.True(File.Exists(Path.Combine(folder, "song.zip")));
        var operations = new StringWriter();
        Assert.Equal(0, await cli.RunAsync(["operations", "--json"], operations, new StringWriter()));
        using var names = JsonDocument.Parse(operations.ToString());
        Assert.DoesNotContain(names.RootElement.EnumerateArray(), o => o.GetProperty("Id").GetString()!.Contains("Recipe"));
        Assert.DoesNotContain(typeof(CliApplication).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Avalonia"));
    }

    [Theory]
    [InlineData(false, false, 1, 0)]
    [InlineData(true, false, 1, 1)]
    [InlineData(false, true, 1, 130)]
    [InlineData(false, false, 0, 3)]
    public async Task ExitCodesReflectProcessingOutcome(bool issues, bool cancelled, int found, int expected)
    {
        var executor = new FakeExecutor { Result = new(new("Test", DateTimeOffset.Now, cancelled, []), found, issues) };
        Assert.Equal(expected, await new CliApplication(executor).RunAsync(["validate", "x", "--json"], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public async Task CancellationAndReportsNeverOverwriteExistingFiles()
    {
        var executor = new FakeExecutor { Cancel = true };
        Assert.Equal(130, await new CliApplication(executor).RunAsync(["validate", "x"], new StringWriter(), new StringWriter()));
        string report = Path.Combine(TestMediaFixture.NewFolder(), "report.json"); File.WriteAllText(report, "keep");
        executor.Request = null;
        Assert.Equal(2, await new CliApplication(executor).RunAsync(["validate", "x", "--report", report], new StringWriter(), new StringWriter()));
        Assert.Null(executor.Request); Assert.Equal("keep", File.ReadAllText(report));
        File.Delete(report); executor.Cancel = false;
        Assert.Equal(0, await new CliApplication(executor).RunAsync(["validate", "x", "--report", report, "--quiet"], new StringWriter(), new StringWriter()));
        using var json = JsonDocument.Parse(File.ReadAllText(report)); Assert.Equal("Test", json.RootElement.GetProperty("Operation").GetString());
    }

    [Fact]
    public async Task RealExtractionPreservesOriginalAndConflictFailsThenSkips()
    {
        string root = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(root, OperationId.ZipToBin, "keep");
        var cli = new CliApplication(new OperationExecutionService(new(TestMediaFixture.NewFolder())));
        string zip = Path.Combine(root, "song.zip");
        Assert.Equal(0, await cli.RunAsync(["run", "zip-to-audiog", zip, "--no-cue-text", "--quiet"], new StringWriter(), new StringWriter()));
        Assert.True(File.Exists(zip)); Assert.True(File.Exists(Path.Combine(root, "song.wav")));
        Assert.Equal(1, await cli.RunAsync(["run", "zip-to-audiog", zip, "--no-cue-text", "--quiet"], new StringWriter(), new StringWriter()));
        Assert.Equal(0, await cli.RunAsync(["run", "zip-to-audiog", zip, "--overwrite", "skip", "--no-cue-text", "--quiet"], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public async Task RealTagPreviewIsReadOnlyAndTagReportsInvalidPatternMatches()
    {
        string folder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(folder, OperationId.AudioGToBin, "keep");
        string audio = Path.Combine(folder, "DISC-00 - Artist - Song.wav"); File.Move(Path.Combine(folder, "song.wav"), audio);
        string before = FileTransaction.Hash(audio);
        var cli = new CliApplication(new OperationExecutionService(new(TestMediaFixture.NewFolder()))); var output = new StringWriter();
        Assert.Equal(0, await cli.RunAsync(["tag-preview", audio, "--json", "--quiet"], output, new StringWriter()));
        Assert.Equal(before, FileTransaction.Hash(audio)); using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("Artist", json.RootElement.GetProperty("TagPreview")[0].GetProperty("Tags").GetProperty("Artist").GetString());
        Assert.Equal(0, await cli.RunAsync(["tag", audio, "--quiet"], new StringWriter(), new StringWriter()));
        Assert.Equal("Song", KaraokeTaggingService.ReadTags(audio).Title);
        Assert.Equal(1, await cli.RunAsync(["tag", audio, "--pattern", "literal %TRACK%", "--quiet"], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public async Task CliProcessRunsWithoutGuiAndReturnsVersionAndUsageExitCode()
    {
        foreach (var (arguments, expected) in new[] { (new[] { "--version" }, 0), (new[] { "recipe", "unknown", "x", "--json" }, 2) })
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            Assert.Equal(expected, process.ExitCode); string text = await stdout; Assert.Empty(await stderr);
            if (expected == 0) Assert.Equal(ApplicationInfo.Version, text.Trim()); else { using var json = JsonDocument.Parse(text); Assert.Equal(2, json.RootElement.GetProperty("ExitCode").GetInt32()); }
        }
    }
}
