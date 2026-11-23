using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KaraokeToolkit.Core;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.DiscRipping;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Cli;

/// <summary>Console adapter only. All file-processing rules remain in Core.</summary>
public sealed class CliApplication(IOperationExecutor? executor = null, IDiscReader? discReader = null, SharedSettingsStore? profile = null)
{
    private readonly IOperationExecutor executor = executor ?? new OperationExecutionService();
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token = default)
    {
        bool json = args.TakeWhile(a => a != "--").Contains("--json");
        try
        {
            if (args.Length > 0 && args[0] == "settings")
                return await RunSettingsAsync(args[1..], output, token);
            Func<SharedSettingsSnapshot>? loadProfile = profile == null ? null : profile.Load;
            var advanced = await new CliAdvancedCommands(executor as OperationExecutionService ?? new(), discReader, loadProfile).TryRunAsync(args, output, error, token);
            if (advanced is { } advancedCode) return advancedCode;
            var parsed = CliArguments.Parse(args, loadProfile); json = parsed.Json;
            if (parsed.Command == "help") { await output.WriteLineAsync(CliHelp.For(parsed.HelpTopic)); return 0; }
            if (parsed.Command == "version") { await output.WriteLineAsync(ApplicationInfo.Version); return 0; }
            if (parsed.Command == "operations")
            {
                var descriptions = CliOperationCatalog.Exposed.Select(o => new { Command = o.Name, Id = o.OperationId, OperationCatalog.Get(o.OperationId).Description, OperationCatalog.Get(o.OperationId).RequiresFolders, o.DedicatedCommand, OperationCatalog.Get(o.OperationId).Support }).ToArray();
                if (json) await output.WriteLineAsync(JsonSerializer.Serialize(descriptions, JsonOptions));
                else
                {
                    await output.WriteLineAsync($"{"Command",-30} {"Core ID",-23} Description");
                    foreach (var operation in descriptions)
                        await output.WriteLineAsync($"{operation.Command,-30} {operation.Id,-23} {operation.Description}" + (operation.RequiresFolders ? " (folders only)" : "") + (operation.DedicatedCommand ? " (dedicated command; use --help)" : ""));
                }
                return 0;
            }
            var recoveryService = executor as OperationExecutionService ?? new();
            if (parsed.Command == "recovery-list")
            {
                var jobs = recoveryService.FindRecovery(parsed.Inputs);
                if (json) await output.WriteLineAsync(JsonSerializer.Serialize(jobs, JsonOptions));
                else foreach (var job in jobs) await output.WriteLineAsync($"{job.Action}: {job.Completed}/{job.Total} — {job.JournalPath}" +
                    (job.Problem != null ? " — " + job.Problem : job.ContinueBatchAfterRecovery ? "" : " — recover interrupted item only"));
                return 0;
            }
            var progress = parsed.Quiet ? null : new ConsoleProgress(error);
            var result = parsed.Command == "recover"
                ? await recoveryService.ResumeAsync(parsed.Journal!, progress, token)
                : parsed.DryRun ? await new OperationPlanner().PlanAsync(parsed.Request!, token)
                : await executor.ExecuteAsync(parsed.Request!, progress, token);
            return await WriteResultAsync(parsed, result, output, error);
        }
        catch (OperationCanceledException) { await Failure(output, error, json, "Cancelled. Completed changes remain committed.", 130); return 130; }
        catch (Exception failure) when (failure is CliUsageException or ArgumentException)
        { await Failure(output, error, json, failure.Message, 2); return 2; }
        catch (Exception failure)
        { await Failure(output, error, json, failure.Message, 1); return 1; }
    }

    private async Task<int> RunSettingsAsync(string[] args, TextWriter output, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var store = profile ?? SharedSettingsStore.ForCurrentUser();
        switch (args)
        {
            case [] or ["--help"] or ["-h"]:
                await output.WriteLineAsync("Usage: karaoke-toolkit settings list | get ID | set ID VALUE");
                break;
            case ["list"]:
                foreach (var setting in store.List(SettingsClient.Cli))
                    await output.WriteLineAsync($"{setting.Id} = {setting.Value} ({setting.Type})");
                break;
            case ["get", var id]:
                await output.WriteLineAsync(store.Get(id, SettingsClient.Cli).Value.ToString());
                break;
            case ["set", var id, var value]:
                var saved = store.Set(id, value, SettingsClient.Cli);
                await output.WriteLineAsync($"{saved.Id} = {saved.Value}");
                break;
            default:
                throw new CliUsageException("Use settings list, settings get ID, or settings set ID VALUE.");
        }
        return 0;
    }

    internal static async Task<int> WriteResultAsync(CliArguments parsed, ExecutionResult result, TextWriter output, TextWriter error)
    {
            bool json = parsed.Json;
            if (parsed.ReportPath != null)
            {
                string text = Path.GetExtension(parsed.ReportPath).Equals(".json", StringComparison.OrdinalIgnoreCase) ? result.Report.ToJson() : result.Report.ToText();
                // CreateNew protects existing files even if one appeared after argument parsing.
                await using var stream = new FileStream(parsed.ReportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await writer.WriteAsync(text);
            }
            int exitCode = result.Report.Cancelled ? 130 : result.HasIssues ? 1 : result.Found == 0 ? 3 : 0;
            if (json && result.AudioKeyPreview != null)
                await output.WriteLineAsync(JsonSerializer.Serialize(new { ExitCode = exitCode, result.Found, result.HasIssues, result.Report, result.AudioKeyPreview }, JsonOptions));
            else if (json && result.AudioGainPreview != null)
                await output.WriteLineAsync(JsonSerializer.Serialize(new { ExitCode = exitCode, result.Found, result.HasIssues, result.Report, result.AudioGainPreview }, JsonOptions));
            else if (json) await output.WriteLineAsync(JsonSerializer.Serialize(new { ExitCode = exitCode, result.Found, result.HasIssues, result.Report, result.TagPreview }, JsonOptions));
            else
            {
                await output.WriteLineAsync(result.Report.ToText());
                if (result.TagPreview != null)
                    foreach (var row in result.TagPreview.Where(p => p.CanApply))
                        await output.WriteLineAsync($"{row.Path}: Album={row.Tags!.Album}; Track={row.Tags.Track}; Artist={row.Tags.Artist}; Title={row.Tags.Title}");
                if (exitCode == 3) await error.WriteLineAsync("No matching items found.");
            }
            return exitCode;
    }

    private static Task Failure(TextWriter output, TextWriter error, bool json, string message, int code) =>
        json ? output.WriteLineAsync(JsonSerializer.Serialize(new { ExitCode = code, Error = message }, JsonOptions)) : error.WriteLineAsync(message);

    internal sealed class ConsoleProgress(TextWriter writer) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) { lock (writer) writer.WriteLine($"[{value.TotalPercent}%] {value.Message}"); }
    }

    public const string Help = """
        Karaoke Toolkit CLI

        Usage:
          karaoke-toolkit --help
          karaoke-toolkit operations [--json]
          karaoke-toolkit run OPERATION PATH... [options]
          karaoke-toolkit validate-karaoke-files PATH... [options]
          karaoke-toolkit preview-tags PATH... [options]
          karaoke-toolkit tag-karaoke-files PATH... [options]
          karaoke-toolkit recovery-list [FOLDER...] [--json]
          karaoke-toolkit recover JOURNAL [--json] [--quiet] [--report PATH]
          karaoke-toolkit recipe list|show|validate|run ...
          karaoke-toolkit quarantine PATH... [--dry-run] [--no-recurse]
          karaoke-toolkit disc-list [--cdrdao EXECUTABLE] [--json]
          karaoke-toolkit rip-cdg --device DEVICE --output FOLDER --disc-name NAME --experimental
          karaoke-toolkit completion powershell|bash|zsh
          karaoke-toolkit path powershell|bash|zsh
          karaoke-toolkit settings list | get ID | set ID VALUE
          karaoke-toolkit help [COMMAND|OPERATION]
          karaoke-toolkit --version

        PATH may also be specified with repeated --input PATH. Use -- before paths
        beginning with a dash. See operations for command names.
        No GUI or prompts. Recipe steps use their saved settings and cannot use Ask.
        Quarantine is explicit; physical disc capture requires --experimental.

        Output: --json (one JSON document on stdout), --quiet (no progress on stderr),
                --report NEW.json|NEW.txt (never overwrites an existing file)
        Processing:
          --dry-run                    Read-only plan; not an encoding guarantee
          --output FOLDER              Media conversions and Group by Track ID
          --options FILE.json          Override shared processing settings; flags override the file
          --overwrite fail|skip|replace (saved Ask uses fail)
          --delete-originals | --keep-originals (default keep)
          --recurse | --no-recurse      (default recurse)
          --cue-text | --no-cue-text    (default generate where applicable)
          --validate-inputs | --no-validate-inputs
          --encoder MP3|OGG|WMA|WAV|FLAC|AAC   --bitrate KBPS (default MP3, 320)
          --zip-algorithm Deflate|BZip2|LZMA   --zip-level 0..9 (default 5)
          --ignore-leading-the | --no-ignore-leading-the (Group by Alphabet — Files)
          Deflate is the default; legacy karaoke software generally prefers it.
          Shared processing settings apply where supported by the chosen operation.
          Group by Alphabet (Files) always recurses. Folders groups immediate children.
          Flatten depth is chosen by the operation, independently of --no-recurse.
          --recovery-verification sha256|xxhash128|metadata|none (default xxhash128 for standalone media conversions and file moves)
          --check-timing before-batch|before-item  More robust waiting-file checks or faster startup (default before-item)
                                                  Applies to standalone media conversions, file moves and grouping.
          --batch-resume | --no-batch-resume  Continue the batch or recover only the interrupted item
          See help group-by-track-id for track grouping and folder naming options.
        Tagging:
          --pattern "%DISC%-%TRACK% - %ARTIST% - %TITLE%"
          --fill-empty-only --preserve-comments --preserve-other-tags
          Tags update in place; delete/overwrite options do not affect tagging.
          Use preview-tags first to review inferred tags without changing files.
        Video:
          --video-container MP4|MKV|AVI --video-resolution SD|HD|FullHD|QHD|UHD
          --video-smoothing None|Bilinear|Bicubic|Lanczos
          --video-frame-rate 25|30|60 --video-bitrate 1000|2000|4000|6000
          --video-audio-bitrate 128|192|256|320 --video-audio-fallback-only
          Defaults: MP4, HD (720p), no smoothing, 30 fps, 2000 kbps video.
          Audio matches source bitrate where detectable, with 192 kbps fallback.
          --video-audio-bitrate changes the fallback; --video-audio-fallback-only
          always uses that rate. Video flags require run karaoke-to-video.

        Examples:
          karaoke-toolkit validate-karaoke-files "D:\Karaoke" --json --quiet
          karaoke-toolkit run zip-to-audiog "D:\Incoming" --no-cue-text
          karaoke-toolkit run repack-zip-for-compatibility "D:\Incoming" --overwrite skip
          karaoke-toolkit preview-tags "D:\Karaoke" --pattern "%ARTIST% - %TITLE%"
          karaoke-toolkit run group-by-alphabet-files "D:\Karaoke" --dry-run
          karaoke-toolkit run karaoke-to-video "D:\Karaoke" --video-container MP4

        Option names are case-sensitive; command and enum choices ignore case.
        JSON enum settings use names, except ZipCompression which also accepts 0-9.
        Flags override --options FILE.json; desktop preferences are not loaded.
        See docs/user/cli.md in the source repository for workflows and settings examples.

        Ctrl+C cancels remaining work; completed groups remain committed.
        Recovery uses saved settings, including recipe snapshots and remaining steps.
        Exit codes: 0 success, 1 processing/validation failure, 2 invalid usage,
                    3 no matching items, 130 cancelled.
        """;
}
