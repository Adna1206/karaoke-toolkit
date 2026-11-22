using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Tagging;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Cli;

public sealed class CliUsageException(string message) : Exception(message);

public sealed record CliArguments(string Command, OperationRequest? Request, string[] Inputs, string? Journal, bool Json, bool Quiet, string? ReportPath)
{
    public bool DryRun { get; init; }
    public string? HelpTopic { get; init; }
    internal static readonly string[] GroupingFlags = ["whole-folders", "move", "create-parents", "keep-single-child-parents"];
    internal static readonly string[] GroupingValues = ["trailing-digits", "range-size", "range-start", "id-separator",
        "range-separator", "mask", "max-parent-levels"];
    private static readonly HashSet<string> Flags = ["json", "quiet", "recurse", "no-recurse", "delete-originals", "keep-originals", "cue-text", "no-cue-text",
        "validate-inputs", "no-validate-inputs", "ignore-leading-the", "no-ignore-leading-the", "fill-empty-only", "preserve-comments", "preserve-other-tags", "video-audio-fallback-only", "dry-run", "batch-resume", "no-batch-resume"];
    private static readonly HashSet<string> Values = ["input", "options", "overwrite", "encoder", "bitrate", "zip-level", "zip-algorithm", "pattern", "report",
        "video-container", "video-resolution", "video-smoothing", "video-frame-rate", "video-bitrate", "video-audio-bitrate", "output", "recovery-verification"];
    public static CliArguments Parse(string[] args, Func<SharedSettingsSnapshot>? loadProfile = null)
    {
        if (args.Length == 0 || args.TakeWhile(a => a != "--").Any(a => a is "--help" or "-h")) return new("help", null, [], null, false, false, null)
        { HelpTopic = args.Length > 1 && args[0] == "run" && !args[1].StartsWith('-') ? args[1] : args.FirstOrDefault(a => !a.StartsWith('-')) };
        if (args.Length == 1 && args[0] == "--version") return new("version", null, [], null, false, false, null);
        string command = args[0].ToLowerInvariant();
        int start = 1;
        OperationId? operation = null;
        if (command == "run")
        {
            if (args.Length < 2 || args[1].StartsWith('-')) throw new CliUsageException("run requires an operation. Use operations to list choices.");
            string key = args[1]; start = 2;
            var registration = CliOperationCatalog.Resolve(key);
            if (registration.DedicatedCommand) throw new CliUsageException("Use " + registration.Name + " directly; see " + registration.Name + " --help.");
            operation = registration.OperationId;
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
            bool isFlag = Flags.Contains(name) || GroupingFlags.Contains(name);
            bool isValue = Values.Contains(name) || GroupingValues.Contains(name);
            if (!isFlag && !isValue) throw new CliUsageException("Unknown option: --" + name);
            if (isFlag && value != null) throw new CliUsageException("--" + name + " is a flag, not a value option.");
            if (isValue && value == null)
            {
                if (++i >= args.Length || args[i].StartsWith("--")) throw new CliUsageException("Missing value for --" + name);
                value = args[i];
            }
            if (isValue && name is not ("id-separator" or "range-separator") && string.IsNullOrWhiteSpace(value))
                throw new CliUsageException("Empty value for --" + name);
            if (name == "input") { inputs.Add(value!); continue; }
            if (!options.TryAdd(name, value)) throw new CliUsageException("Option repeated: --" + name);
        }
        foreach (var (yes, no) in new[] { ("recurse", "no-recurse"), ("delete-originals", "keep-originals"), ("cue-text", "no-cue-text"),
            ("validate-inputs", "no-validate-inputs"), ("ignore-leading-the", "no-ignore-leading-the"), ("batch-resume", "no-batch-resume") })
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
        var profile = loadProfile?.Invoke();
        var defaults = profile?.ForUnattendedOperation(operation.Value, inputs) ?? new OperationRequest(operation.Value, inputs);
        bool saveResumableRun = options.ContainsKey("batch-resume") || defaults.SaveResumableRun && !options.ContainsKey("no-batch-resume");
        var recoveryVerification = options.GetValueOrDefault("recovery-verification") switch
        {
            null => defaults.RecoveryVerification,
            "sha256" => RecoveryVerificationMode.Sha256,
            "xxhash128" => RecoveryVerificationMode.XxHash128,
            "metadata" => RecoveryVerificationMode.MetadataOnly,
            "none" => RecoveryVerificationMode.None,
            _ => throw new CliUsageException("--recovery-verification expects sha256, xxhash128, metadata or none.")
        };
        if ((options.ContainsKey("recovery-verification") || options.ContainsKey("batch-resume") || options.ContainsKey("no-batch-resume")) &&
            !OperationCapabilities.SupportsFileMoveRecoveryOptions(operation.Value))
            throw new CliUsageException("Recovery options require grouping, alphabetical organization or flattening.");
        if (operation == OperationId.GroupByTrackId)
            return new(command, ParseGrouping(options, inputs) with { RecoveryVerification = recoveryVerification, SaveResumableRun = saveResumableRun }, inputs.ToArray(), null, json, quiet, report)
            { DryRun = options.ContainsKey("dry-run") };
        if (options.Keys.Any(k => GroupingFlags.Contains(k) || GroupingValues.Contains(k)))
            throw new CliUsageException("Track grouping settings require run group-by-track-id.");

        var processing = LoadOptions(options.GetValueOrDefault("options"), defaults.Options, out bool explicitBitrate);
        if (options.ContainsKey("recurse")) processing = processing with { RecurseFolders = true };
        if (options.ContainsKey("no-recurse")) processing = processing with { RecurseFolders = false };
        if (options.ContainsKey("delete-originals")) processing = processing with { DeleteOldFiles = true };
        if (options.ContainsKey("keep-originals")) processing = processing with { DeleteOldFiles = false };
        if (options.ContainsKey("cue-text")) processing = processing with { AutoCreateCueText = true };
        if (options.ContainsKey("no-cue-text")) processing = processing with { AutoCreateCueText = false };
        if (options.TryGetValue("encoder", out string? encoder)) processing = processing with { AudioEncoder = EnumValue<AudioEncoder>(encoder!) };
        if (!explicitBitrate && profile != null)
            processing = processing with
            {
                AudioBitrate = profile.BitrateFor(operation == OperationId.LosslessGToMp3G ? AudioEncoder.MP3 : processing.AudioEncoder, processing.AudioBitrate)
            };
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
        var overwrite = options.TryGetValue("overwrite", out string? policy) ? EnumValue<OverwritePolicy>(policy!) : defaults.OverwritePolicy;
        if (overwrite == OverwritePolicy.Ask) throw new CliUsageException("--overwrite must be fail, skip or replace; the CLI never prompts.");
        string pattern = options.GetValueOrDefault("pattern") ?? defaults.TaggingPattern;
        bool tagging = operation is OperationId.TagKaraoke or OperationId.PreviewTags;
        if (!tagging && options.Keys.Any(k => k is "pattern" or "fill-empty-only" or "preserve-comments" or "preserve-other-tags"))
            throw new CliUsageException("Tag settings require tag or tag-preview.");
        if (operation != OperationId.KaraokeToVideo && options.Keys.Any(k => k.StartsWith("video-", StringComparison.Ordinal)))
            throw new CliUsageException("Video settings require run karaoke-to-video.");
        if (operation != OperationId.OrganizeFiles && (options.ContainsKey("ignore-leading-the") || options.ContainsKey("no-ignore-leading-the")))
            throw new CliUsageException("--ignore-leading-the requires run organize-files.");
        if (tagging) _ = new FilenameTagPattern(pattern);
        var capability = OperationCatalog.Get(operation.Value);
        string outputFolder = options.TryGetValue("output", out var destination) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination!)) : "";
        if (outputFolder.Length > 0 && (!capability.IsConversion ||
            OperationCapabilities.RenamesFilenames(capability.Id) || capability.Id == OperationId.CreateCueAndTextFiles))
            throw new CliUsageException("--output is available for media conversions only.");
        new ConversionRequest(capability.IsConversion ? capability.Id : OperationId.AudioGToZip, ".", processing) { OutputFolder = outputFolder }.Validate();
        return new(command, new(operation.Value, inputs.ToArray())
        {
            RecoveryVerification = recoveryVerification, SaveResumableRun = saveResumableRun,
            Options = processing, OverwritePolicy = overwrite,
            ValidateBeforeConversion = !options.ContainsKey("no-validate-inputs") && (options.ContainsKey("validate-inputs") || defaults.ValidateBeforeConversion),
            OutputFolder = outputFolder,
            IgnoreLeadingThe = !options.ContainsKey("no-ignore-leading-the") && (options.ContainsKey("ignore-leading-the") || defaults.IgnoreLeadingThe), TaggingPattern = pattern,
            Tagging = new(options.ContainsKey("fill-empty-only"), !options.ContainsKey("preserve-comments"), !options.ContainsKey("preserve-other-tags"))
        }, inputs.ToArray(), null, json, quiet, report) { DryRun = options.ContainsKey("dry-run") };
    }

    private static OperationRequest ParseGrouping(Dictionary<string, string?> options, List<string> inputs)
    {
        foreach (string name in options.Keys)
            if (name is not ("json" or "quiet" or "report" or "dry-run" or "output" or "recurse" or "no-recurse" or "recovery-verification" or "batch-resume" or "no-batch-resume")
                && !GroupingFlags.Contains(name) && !GroupingValues.Contains(name))
                throw new CliUsageException($"--{name} is unavailable for group-by-track-id.");

        var output = options.GetValueOrDefault("output") ?? throw new CliUsageException("group-by-track-id requires --output FOLDER.");
        var defaults = new TrackGroupingOptions();
        int Setting(string name, int fallback) => options.TryGetValue(name, out var value) ? Number(value!, name) : fallback;
        int rangeSize = Setting("range-size", defaults.RangeSize);
        if (options.ContainsKey("range-size") && rangeSize == 0)
            throw new CliUsageException("--range-size must be positive.");
        if (rangeSize > 0 && options.Keys.Any(k => k is "trailing-digits" or "mask" or "create-parents" or "max-parent-levels" or "keep-single-child-parents"))
            throw new CliUsageException("Trailing-digit settings cannot be combined with --range-size.");
        if (rangeSize == 0 && options.Keys.Any(k => k is "range-start" or "range-separator"))
            throw new CliUsageException("--range-start and --range-separator require --range-size.");
        if (!options.ContainsKey("create-parents") && options.Keys.Any(k => k is "max-parent-levels" or "keep-single-child-parents"))
            throw new CliUsageException("Parent settings require --create-parents.");
        if (options.ContainsKey("whole-folders") && options.ContainsKey("recurse"))
            throw new CliUsageException("--recurse scans folder contents and cannot be combined with --whole-folders.");

        return new(OperationId.GroupByTrackId, inputs.ToArray())
        {
            OutputFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output)),
            Grouping = defaults with
            {
                WholeFolders = options.ContainsKey("whole-folders"),
                Recurse = options.ContainsKey("recurse"),
                Move = options.ContainsKey("move"),
                CreateParents = options.ContainsKey("create-parents"),
                SkipSingleChildParents = !options.ContainsKey("keep-single-child-parents"),
                TrailingDigits = Setting("trailing-digits", defaults.TrailingDigits),
                RangeSize = rangeSize,
                RangeStart = Setting("range-start", defaults.RangeStart),
                MaxParentLevels = Setting("max-parent-levels", defaults.MaxParentLevels),
                IdSeparator = options.GetValueOrDefault("id-separator") ?? defaults.IdSeparator,
                RangeSeparator = options.GetValueOrDefault("range-separator") ?? defaults.RangeSeparator,
                Mask = options.GetValueOrDefault("mask") ?? defaults.Mask
            }
        };
    }

    private static int Number(string text, string name) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
        ? number : throw new CliUsageException("--" + name + " requires a nonnegative whole number.");
    private static T EnumValue<T>(string text) where T : struct, Enum => Enum.TryParse<T>(text, true, out var value) && Enum.IsDefined(value) && !int.TryParse(text, out _)
        ? value : throw new CliUsageException($"Invalid {typeof(T).Name}: {text}. Choose {string.Join(", ", Enum.GetNames<T>())}.");

    private static ConversionOptions LoadOptions(string? file, ConversionOptions defaults, out bool explicitBitrate)
    {
        explicitBitrate = false;
        if (file == null) return defaults;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected an options object.");
            var names = typeof(ConversionOptions).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!names.Contains(property.Name) || !seen.Add(property.Name)) throw new JsonException("Unknown or repeated setting: " + property.Name);
            var settings = new JsonSerializerOptions(); settings.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            var normalized = JsonSerializer.SerializeToNode(defaults, settings)!.AsObject();
            foreach (var field in document.RootElement.EnumerateObject())
                normalized[field.Name] = JsonNode.Parse(field.Value.GetRawText());
            // Core's persisted-data converter supports historical ZIP presets. CLI
            // settings use today's levels, accepting numbers, numeric strings or names.
            if (document.RootElement.TryGetProperty(nameof(ConversionOptions.ZipCompression), out var compression))
            {
                string text = compression.ValueKind == JsonValueKind.Number ? compression.GetInt32().ToString(CultureInfo.InvariantCulture) : compression.GetString()!;
                int level = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : (int)EnumValue<ZipCompression>(text);
                normalized[nameof(ConversionOptions.ZipCompression)] = ((int)ZipCompressionMigration.FromLegacy(level)).ToString(CultureInfo.InvariantCulture);
            }
            explicitBitrate = document.RootElement.TryGetProperty(nameof(ConversionOptions.AudioBitrate), out _);
            return normalized.Deserialize<ConversionOptions>(settings)!;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or FormatException or NotSupportedException or System.Reflection.TargetInvocationException)
        { throw new CliUsageException("Cannot load --options: " + error.Message); }
    }
}
