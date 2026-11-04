using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Tagging;
using KaraokeToolkit.Core.Video;

namespace KaraokeToolkit.Cli;

public sealed class CliUsageException(string message) : Exception(message);

public sealed record CliArguments(string Command, OperationRequest? Request, string[] Inputs, string? Journal, bool Json, bool Quiet, string? ReportPath)
{
    private static readonly HashSet<string> Flags = ["json", "quiet", "recurse", "no-recurse", "delete-originals", "keep-originals", "cue-text", "no-cue-text",
        "validate-inputs", "ignore-leading-the", "fill-empty-only", "preserve-comments", "preserve-other-tags", "video-audio-fallback-only"];
    private static readonly HashSet<string> Values = ["input", "options", "overwrite", "encoder", "bitrate", "zip-level", "zip-algorithm", "pattern", "report",
        "video-container", "video-resolution", "video-smoothing", "video-frame-rate", "video-bitrate", "video-audio-bitrate"];
    public static CliArguments Parse(string[] args)
    {
        if (args.Length == 0 || args.TakeWhile(a => a != "--").Any(a => a is "--help" or "-h")) return new("help", null, [], null, false, false, null);
        if (args.Length == 1 && args[0] == "--version") return new("version", null, [], null, false, false, null);
        string command = args[0].ToLowerInvariant();
        int start = 1;
        OperationId? operation = null;
        if (command == "run")
        {
            if (args.Length < 2 || args[1].StartsWith('-')) throw new CliUsageException("run requires an operation. Use operations to list choices.");
            string key = args[1]; start = 2;
            operation = CliOperationCatalog.Resolve(key).OperationId;
        }
        else if (command is "validate" or "tag" or "tag-preview") operation = CliOperationCatalog.Resolve(command).OperationId;
        else if (command is not ("operations" or "recovery-list" or "recover")) throw new CliUsageException("Unknown command: " + args[0]);

        var inputs = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        bool positional = false;
        for (int i = start; i < args.Length; i++)
        {
            string argument = args[i];
            if (argument == "--" && !positional) { positional = true; continue; }
            if (positional || !argument.StartsWith('-')) { inputs.Add(argument); continue; }
            if (!argument.StartsWith("--")) throw new CliUsageException("Unknown option: " + argument);
            string[] pair = argument[2..].Split('=', 2); string name = pair[0]; string? value = pair.Length == 2 ? pair[1] : null;
            if (!Flags.Contains(name) && !Values.Contains(name)) throw new CliUsageException("Unknown option: --" + name);
            if (Flags.Contains(name) && value != null) throw new CliUsageException("--" + name + " is a flag, not a value option.");
            if (Values.Contains(name) && value == null)
            {
                if (++i >= args.Length || args[i].StartsWith("--")) throw new CliUsageException("Missing value for --" + name);
                value = args[i];
            }
            if (Values.Contains(name) && string.IsNullOrWhiteSpace(value)) throw new CliUsageException("Empty value for --" + name);
            if (name == "input") { inputs.Add(value!); continue; }
            if (!options.TryAdd(name, value)) throw new CliUsageException("Option repeated: --" + name);
        }
        foreach (var (yes, no) in new[] { ("recurse", "no-recurse"), ("delete-originals", "keep-originals"), ("cue-text", "no-cue-text") })
            if (options.ContainsKey(yes) && options.ContainsKey(no)) throw new CliUsageException($"Cannot combine --{yes} and --{no}.");

        bool json = options.ContainsKey("json"), quiet = options.ContainsKey("quiet");
        string? report = options.GetValueOrDefault("report");
        if (report != null)
        {
            report = Path.GetFullPath(report);
            if (Path.GetExtension(report).ToLowerInvariant() is not (".json" or ".txt")) throw new CliUsageException("Report must have a .json or .txt extension.");
            if (File.Exists(report) || Directory.Exists(report)) throw new CliUsageException("Report already exists; reports are never overwritten: " + report);
            if (!Directory.Exists(Path.GetDirectoryName(report))) throw new CliUsageException("Report directory does not exist.");
        }
        if (operation == null)
        {
            if (options.Keys.Any(k => k is not ("json" or "quiet" or "report"))) throw new CliUsageException("Processing settings require run, validate, tag or tag-preview.");
            if (command == "operations" && inputs.Count > 0) throw new CliUsageException("operations does not take input paths.");
            if (command == "recover" && inputs.Count != 1) throw new CliUsageException("recover requires exactly one journal path from recovery-list.");
            if (command is "operations" or "recovery-list" && report != null) throw new CliUsageException("--report is only available for operation results.");
            return new(command, null, inputs.ToArray(), command == "recover" ? inputs[0] : null, json, quiet, report);
        }
        if (inputs.Count == 0) throw new CliUsageException("At least one input path is required. Use --input PATH or positional paths.");

        var processing = LoadOptions(options.GetValueOrDefault("options"));
        if (options.ContainsKey("recurse")) processing = processing with { RecurseFolders = true };
        if (options.ContainsKey("no-recurse")) processing = processing with { RecurseFolders = false };
        if (options.ContainsKey("delete-originals")) processing = processing with { DeleteOldFiles = true };
        if (options.ContainsKey("keep-originals")) processing = processing with { DeleteOldFiles = false };
        if (options.ContainsKey("cue-text")) processing = processing with { AutoCreateCueText = true };
        if (options.ContainsKey("no-cue-text")) processing = processing with { AutoCreateCueText = false };
        if (options.TryGetValue("encoder", out string? encoder)) processing = processing with { AudioEncoder = EnumValue<AudioEncoder>(encoder!) };
        if (options.TryGetValue("bitrate", out string? bitrate)) processing = processing with { AudioBitrate = Number(bitrate!, "bitrate") };
        if (options.TryGetValue("zip-level", out string? level)) processing = processing with { ZipCompression = ZipCompressionMigration.FromLegacy(Number(level!, "zip-level")) };
        if (options.TryGetValue("zip-algorithm", out string? algorithm)) processing = processing with { ZipAlgorithm = EnumValue<ZipAlgorithm>(algorithm!) };
        if (options.TryGetValue("video-container", out string? container)) processing = processing with { VideoContainer = EnumValue<VideoContainer>(container!) };
        if (options.TryGetValue("video-resolution", out string? resolution)) processing = processing with { VideoResolution = EnumValue<VideoResolution>(resolution!) };
        if (options.TryGetValue("video-smoothing", out string? smoothing)) processing = processing with { VideoGraphicsSmoothing = EnumValue<VideoGraphicsSmoothing>(smoothing!) };
        if (options.TryGetValue("video-frame-rate", out string? fps)) processing = processing with { VideoFrameRate = Number(fps!, "video-frame-rate") };
        if (options.TryGetValue("video-bitrate", out string? videoRate)) processing = processing with { VideoBitrateKbps = Number(videoRate!, "video-bitrate") };
        if (options.TryGetValue("video-audio-bitrate", out string? audioRate)) processing = processing with { VideoAudioBitrateKbps = Number(audioRate!, "video-audio-bitrate") };
        if (options.ContainsKey("video-audio-fallback-only")) processing = processing with { VideoAudioMatchSourceBitrate = false };
        var overwrite = options.TryGetValue("overwrite", out string? policy) ? EnumValue<OverwritePolicy>(policy!) : OverwritePolicy.Fail;
        if (overwrite == OverwritePolicy.Ask) throw new CliUsageException("--overwrite must be fail, skip or replace; the CLI never prompts.");
        string pattern = options.GetValueOrDefault("pattern") ?? FilenameTagPattern.Default;
        bool tagging = operation is OperationId.TagKaraoke or OperationId.PreviewTags;
        if (!tagging && options.Keys.Any(k => k is "pattern" or "fill-empty-only" or "preserve-comments" or "preserve-other-tags"))
            throw new CliUsageException("Tag settings require tag or tag-preview.");
        if (operation != OperationId.KaraokeToVideo && options.Keys.Any(k => k.StartsWith("video-", StringComparison.Ordinal)))
            throw new CliUsageException("Video settings require run karaoke-to-video.");
        if (operation != OperationId.OrganizeFiles && options.ContainsKey("ignore-leading-the"))
            throw new CliUsageException("--ignore-leading-the requires run organize-files.");
        if (tagging) _ = new FilenameTagPattern(pattern);
        var capability = OperationCatalog.Get(operation.Value);
        new ConversionRequest(capability.IsConversion ? capability.Id : OperationId.AudioGToZip, ".", processing).Validate();
        return new(command, new(operation.Value, inputs.ToArray())
        {
            Options = processing, OverwritePolicy = overwrite, ValidateBeforeConversion = options.ContainsKey("validate-inputs"),
            IgnoreLeadingThe = options.ContainsKey("ignore-leading-the"), TaggingPattern = pattern,
            Tagging = new(options.ContainsKey("fill-empty-only"), !options.ContainsKey("preserve-comments"), !options.ContainsKey("preserve-other-tags"))
        }, inputs.ToArray(), null, json, quiet, report);
    }

    private static int Number(string text, string name) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
        ? number : throw new CliUsageException("--" + name + " requires a nonnegative whole number.");
    private static T EnumValue<T>(string text) where T : struct, Enum => Enum.TryParse<T>(text, true, out var value) && Enum.IsDefined(value) && !int.TryParse(text, out _)
        ? value : throw new CliUsageException($"Invalid {typeof(T).Name}: {text}. Choose {string.Join(", ", Enum.GetNames<T>())}.");

    private static ConversionOptions LoadOptions(string? file)
    {
        if (file == null) return new() { DeleteOldFiles = false };
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected an options object.");
            var names = typeof(ConversionOptions).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!names.Contains(property.Name) || !seen.Add(property.Name)) throw new JsonException("Unknown or repeated setting: " + property.Name);
            var settings = new JsonSerializerOptions(); settings.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            var normalized = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
            // Core's persisted-data converter supports historical ZIP presets. CLI
            // settings use today's levels, accepting numbers, numeric strings or names.
            if (document.RootElement.TryGetProperty(nameof(ConversionOptions.ZipCompression), out var compression))
            {
                string text = compression.ValueKind == JsonValueKind.Number ? compression.GetInt32().ToString(CultureInfo.InvariantCulture) : compression.GetString()!;
                int level = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : (int)EnumValue<ZipCompression>(text);
                normalized[nameof(ConversionOptions.ZipCompression)] = ((int)ZipCompressionMigration.FromLegacy(level)).ToString(CultureInfo.InvariantCulture);
            }
            var result = normalized.Deserialize<ConversionOptions>(settings)!;
            // CLI originals are kept unless explicitly requested in JSON or on the command line.
            return document.RootElement.TryGetProperty(nameof(ConversionOptions.DeleteOldFiles), out _) ? result : result with { DeleteOldFiles = false };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or FormatException or NotSupportedException or System.Reflection.TargetInvocationException)
        { throw new CliUsageException("Cannot load --options: " + error.Message); }
    }
}
