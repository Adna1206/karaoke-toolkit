using System.ComponentModel;
using System.Text.Json;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recipes;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace KaraokeToolkit.Mcp;

[McpServerToolType]
public sealed class McpTools(McpOperationsService service, McpAccessPolicy policy)
{
    [McpServerTool(Name = "get_operations", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Discover every Core operation, its explicit MCP exposure decision, file-change requirement and exclusion reason.")]
    public object GetOperations() => new
    {
        policy.AllowFileChanges,
        Operations = McpOperationCatalog.All.Select(r => new { Operation = r.Id, r.Included, r.ChangesFiles, r.ExclusionReason, OperationCatalog.Get(r.Id).Description, OperationCatalog.Get(r.Id).RequiresFolders })
    };

    [McpServerTool(Name = "get_settings", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Discover per-call defaults, encoding choices and track grouping styles. Desktop preferences are never loaded. Use named enum values; ZIP levels are strings 0-9.")]
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
        TrackGroupingMasks = new[] { "x", "X", "#", "s" },
        Notes = "Paths are local absolute paths. Originals are kept by default. Media conversions with OutputFolder keep originals. GroupByTrackId requires OutputFolder and accepts Grouping settings. Recipes use saved per-step settings; Ask is unsupported. Read-only mode is not an OS filesystem sandbox."
    };

    [McpServerTool(Name = "validate_karaoke", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Check local karaoke structure, pairing and ZIP integrity without moving or rewriting files. Not a playback or audio-quality test.")]
    public Task<ExecutionResult> Validate(string[] inputs, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken, bool recurse = true)
        => service.RunAsync(new(OperationId.ValidateKaraoke, inputs) { Options = new() { DeleteOldFiles = false, RecurseFolders = recurse } }, false, progress, cancellationToken);

    [McpServerTool(Name = "preview_tags", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read existing tags and preview filename-derived metadata without writing. Pattern tokens: %DISC%, %TRACK%, %ARTIST%, %TITLE%.")]
    public Task<ExecutionResult> PreviewTags(string[] inputs, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken,
        string pattern = KaraokeToolkit.Core.Tagging.FilenameTagPattern.Default, bool recurse = true)
        => service.RunAsync(new(OperationId.PreviewTags, inputs) { TaggingPattern = pattern, Options = new() { DeleteOldFiles = false, RecurseFolders = recurse } }, false, progress, cancellationToken);

    [McpServerTool(Name = "plan_operation", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read-only plan of an included operation's scope, outputs and detectable collisions. Does not guarantee decoding or encoding success. No source/output files or reports are written.")]
    public Task<ExecutionResult> PlanOperation(OperationRequest request, CancellationToken cancellationToken)
        => service.PlanAsync(request, cancellationToken);

    [McpServerTool(Name = "run_operation", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Run an explicitly included Core operation with per-call settings. File changes require owner startup --allow-writes and confirmFileChanges=true after user authorization. Returns a completion report; does not launch CLI or shell scripts.")]
    public Task<ExecutionResult> RunOperation(OperationRequest request, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken, bool confirmFileChanges = false)
        => service.RunAsync(request, confirmFileChanges, progress, cancellationToken);

    [McpServerTool(Name = "run_recipe", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Run a supplied portable recipe with its saved step settings through Core. Every step must be MCP-included and use Fail/Skip/Replace. Requires startup --allow-writes and user-authorized confirmFileChanges=true; no arbitrary script steps.")]
    public Task<ExecutionResult> RunRecipe(RecipeDefinition recipe, string[] inputs, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken, bool confirmFileChanges = false)
        => service.RecipeAsync(recipe, inputs, confirmFileChanges, progress, cancellationToken);

    [McpServerTool(Name = "list_recovery", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Discover pending local conversion, tagging, track grouping or recipe journals. Optional folders limit discovery; omitted folders use the local recovery index. Does not resume anything.")]
    public object ListRecovery(string[]? folders = null) => service.ListRecovery(folders ?? []);

    [McpServerTool(Name = "recover", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Resume an available journal returned by list_recovery, using saved settings and recipe snapshots. Requires startup --allow-writes and user-authorized confirmFileChanges=true. Changed files or damaged journals remain protected.")]
    public Task<ExecutionResult> Recover(string journal, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken, bool confirmFileChanges = false)
        => service.ResumeAsync(journal, confirmFileChanges, progress, cancellationToken);
}
