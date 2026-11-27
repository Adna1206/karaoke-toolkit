using System.ComponentModel;
using System.Text.Json;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Settings;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace KaraokeToolkit.Mcp;

[McpServerToolType]
public sealed class McpTools(McpOperationsService service, McpAccessPolicy policy, SharedSettingsStore profile)
{
    [McpServerTool(Name = "get_operations", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Discover operation names shared with CLI, stable Core IDs, MCP availability and file-change requirements.")]
    public object GetOperations() => new
    {
        policy.AllowFileChanges,
        Operations = McpOperationCatalog.All.Select(r => new { r.Name, CoreId = r.Id.ToString(), r.Included, r.ChangesFiles, r.ExclusionReason, OperationCatalog.Get(r.Id).Description, OperationCatalog.Get(r.Id).RequiresFolders })
    };

    [McpServerTool(Name = "get_settings", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Discover built-in defaults, encoding choices and track grouping styles. Use list_settings to read the shared processing profile.")]
    public object GetSettings() => new
    {
        Defaults = new OperationRequest(OperationId.ValidateKaraoke, []),
        AudioEncoders = Enum.GetNames<KaraokeToolkit.Core.Audio.AudioEncoder>(),
        ZipAlgorithms = Enum.GetNames<KaraokeToolkit.Core.Archives.ZipAlgorithm>(),
        ZipLevels = Enumerable.Range(0, 10).Select(n => n.ToString()),
        VideoContainers = Enum.GetNames<KaraokeToolkit.Core.Video.VideoContainer>(),
        VideoResolutions = Enum.GetNames<KaraokeToolkit.Core.Video.VideoResolution>(),
        VideoSmoothing = Enum.GetNames<KaraokeToolkit.Core.Video.VideoGraphicsSmoothing>(),
        AudioBitrates = new[] { 64, 96, 128, 160, 192, 224, 256, 320 },
        OverwritePolicies = new[] { "Fail", "Skip", "Replace" },
        RecoveryVerificationModes = Enum.GetNames<KaraokeToolkit.Core.Recovery.RecoveryVerificationMode>(),
        FileCheckTimings = Enum.GetNames<KaraokeToolkit.Core.Recovery.FileCheckTiming>(),
        OperationCapabilities.DefaultFileMoveCheckTiming,
        OperationCapabilities.DefaultFileMoveRecoveryVerification,
        OperationCapabilities.DefaultFileWriteRecoveryVerification,
        TrackGroupingMasks = new[] { "x", "X", "#", "s" },
        AudioGainModes = Enum.GetNames<KaraokeToolkit.Core.Audio.AudioGainMode>(),
        AudioGainExtensions = KaraokeToolkit.Core.Audio.AudioGainService.Extensions,
        Notes = "Use the name from get_operations as operationId. Paths are local absolute paths. Originals are kept by default. Media conversions with OutputFolder keep originals. group-by-track-id requires OutputFolder and accepts Grouping settings. normalize-audio-gain and change-audio-key require OutputFolder and accept AudioGain and AudioKey settings respectively. Recipes use Core IDs and saved per-step settings. Ask is unsupported. Read-only mode is not an OS filesystem sandbox."
    };

    [McpServerTool(Name = "list_settings", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List exposed shared processing preferences, types, choices and effective values.")]
    public object ListSettings() => new { Settings = ReadSettings(() => profile.List(SettingsClient.Mcp)) };

    [McpServerTool(Name = "get_setting", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read one effective setting by its stable ID from list_settings.")]
    public SettingValue GetSetting(string id) => ReadSettings(() => profile.Get(id, SettingsClient.Mcp));

    [McpServerTool(Name = "set_setting", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Save a shared setting by ID with a typed JSON value. Requires --allow-writes and confirmFileChanges=true after user authorization.")]
    public SettingValue SetSetting(string id, JsonElement value, bool confirmFileChanges = false) =>
        ReadSettings(() =>
        {
            policy.RequireFileChanges(confirmFileChanges);
            return profile.Set(id, value, SettingsClient.Mcp);
        });

    private static T ReadSettings<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or System.Xml.XmlException or JsonException)
        { throw new McpException(error.Message, error); }
    }

    [McpServerTool(Name = "validate_karaoke_files", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Check local karaoke structure, pairing and ZIP integrity without moving or rewriting files. Not a playback or audio-quality test.")]
    public Task<ExecutionResult> Validate(string[] inputs, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken, bool? recurse = null)
    {
        var request = ReadSettings(profile.Load).ForUnattendedOperation(OperationId.ValidateKaraoke, inputs);
        return service.RunAsync(request with { Options = request.Options with { RecurseFolders = recurse ?? request.Options.RecurseFolders } }, false, progress, cancellationToken);
    }

    [McpServerTool(Name = "preview_tags", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read existing tags and preview filename-derived metadata without writing. Pattern tokens: %DISC%, %TRACK%, %ARTIST%, %TITLE%.")]
    public Task<ExecutionResult> PreviewTags(string[] inputs, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken,
        string? pattern = null, bool? recurse = null)
    {
        var request = ReadSettings(profile.Load).ForUnattendedOperation(OperationId.PreviewTags, inputs);
        return service.RunAsync(request with
        {
            TaggingPattern = pattern ?? request.TaggingPattern,
            Options = request.Options with { RecurseFolders = recurse ?? request.Options.RecurseFolders }
        }, false, progress, cancellationToken);
    }

    [McpServerTool(Name = "plan_operation", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read-only plan of an included operation's scope, outputs and detectable collisions. Does not guarantee decoding or encoding success. No source/output files or reports are written.")]
    public Task<ExecutionResult> PlanOperation(McpOperationRequest request, CancellationToken cancellationToken)
        => service.PlanAsync(ReadSettings(() => request.Resolve(profile.Load())), cancellationToken);

    [McpServerTool(Name = "run_operation", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Run an explicitly included Core operation with per-call settings. File changes require owner startup --allow-writes and confirmFileChanges=true after user authorization. Returns a completion report; does not launch CLI or shell scripts.")]
    public Task<ExecutionResult> RunOperation(McpOperationRequest request, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken, bool confirmFileChanges = false)
        => service.RunAsync(ReadSettings(() => request.Resolve(profile.Load())), confirmFileChanges, progress, cancellationToken);

    [McpServerTool(Name = "run_recipe", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Run a supplied portable recipe with its saved step settings through Core. Every step must be MCP-included and use Fail/Skip/Replace. Requires startup --allow-writes and user-authorized confirmFileChanges=true; no arbitrary script steps.")]
    public Task<ExecutionResult> RunRecipe(RecipeDefinition recipe, string[] inputs, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken, bool confirmFileChanges = false)
        => service.RecipeAsync(recipe, inputs, confirmFileChanges, progress, cancellationToken);

    [McpServerTool(Name = "list_recovery", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Discover pending local conversion, audio gain, tagging, track grouping or recipe journals. Optional folders limit discovery; omitted folders use the local recovery index. Does not resume anything.")]
    public object ListRecovery(string[]? folders = null) => service.ListRecovery(folders ?? []);

    [McpServerTool(Name = "recover", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Recover a journal returned by list_recovery. The saved ContinueBatchAfterRecovery value indicates whether recovery also processes remaining items. Requires startup --allow-writes and user-authorized confirmFileChanges=true. Changed files or damaged journals remain protected.")]
    public Task<ExecutionResult> Recover(string journal, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken, bool confirmFileChanges = false)
        => service.ResumeAsync(journal, confirmFileChanges, progress, cancellationToken);
}
