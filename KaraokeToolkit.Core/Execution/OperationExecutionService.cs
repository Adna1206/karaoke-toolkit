using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Tagging;
using KaraokeToolkit.Core.Validation;
using KaraokeToolkit.Core.Recipes;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Core.Execution;

public sealed record ExecutionResult(CompletionReport Report, int Found, bool HasIssues)
{
    public IReadOnlyList<TaggingPreview>? TagPreview { get; init; }
}

public interface IOperationExecutor
{
    Task<ExecutionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken token);
}

/// <summary>Reusable unattended adapters to existing engines. Processing, verification and
/// transactions remain in those engines. Physical capture uses its typed disc request.</summary>
public sealed partial class OperationExecutionService(RecoveryStore? recovery = null) : IOperationExecutor
{
    private readonly RecoveryStore recovery = recovery ?? new();

    public async Task<ExecutionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
    {
        var operation = OperationCatalog.Get(request.OperationId);
        if (!operation.CanRunUnattended) throw new NotSupportedException("This operation has no unattended execution contract: " + operation.Identifier);
        if (request.OverwritePolicy == OverwritePolicy.Ask || !Enum.IsDefined(request.OverwritePolicy))
            throw new ArgumentException("Headless execution requires overwrite fail, skip or replace.");
        if (!string.IsNullOrEmpty(request.OutputFolder) && !operation.IsConversion)
            throw new ArgumentException("This operation has no output-folder option.");
        new ConversionRequest(operation.IsConversion ? operation.Id : Action.AudioGToZip, ".", request.Options) { OutputFolder = request.OutputFolder }.Validate();
        if (request.Inputs.Count == 0) throw new ArgumentException("At least one input is required.");
        var inputs = request.Inputs.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(InputSelection.Paths).ToArray();
        foreach (string path in inputs)
        {
            RunSession.CheckPlainPath(path);
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Input does not exist: " + path);
            if (operation.RequiresFolders && !Directory.Exists(path)) throw new ArgumentException("This operation requires folder inputs: " + path);
        }
        token.ThrowIfCancellationRequested();
        if (operation.Id == Action.QuarantineInvalid)
            return await QuarantineAsync(inputs, request.Options.RecurseFolders, progress, token);
        if (!string.IsNullOrEmpty(request.OutputFolder))
        {
            if (inputs.Any(p => Directory.Exists(p) && RunSession.Within(p, request.OutputFolder)))
                throw new ArgumentException("The output folder must be outside selected input folders.");
            if (recovery.Find(request.OutputFolder, true).Count > 0)
                throw new IOException("Recover interrupted work in the output folder first.");
            var plan = await new OperationPlanner().PlanAsync(request, token);
            if (plan.HasIssues) throw new IOException("Output plan has collisions. Run with --dry-run to inspect it.");
        }
        if (operation.Id == Action.ValidateKaraoke)
        {
            var validation = await new ValidationService().ScanAsync(inputs, request.Options.RecurseFolders, progress, token);
            return new(CompletionReport.FromValidation(validation), validation.Findings.Count, validation.Findings.Any(f => f.Status != ValidationStatus.Valid));
        }
        if (operation.Id == Action.PreviewTags)
        {
            var preview = await new KaraokeTaggingService(recovery).PreviewAsync(inputs, request.TaggingPattern, request.Options.RecurseFolders, token);
            return new(new("Preview Karaoke Tags", DateTimeOffset.Now, false,
                preview.Select(p => new ReportEntry(p.Path, p.CanApply ? "Ready" : "Skipped", p.Error ?? "Tags parsed; no files changed.", [])).ToArray())
                { ReportType = "Tag Preview Report" }, preview.Count, preview.Any(p => !p.CanApply)) { TagPreview = preview };
        }
        foreach (string folder in inputs.Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p)!).Distinct(InputSelection.Paths))
            if (recovery.Find(folder, request.Options.RecurseFolders || operation.Id is Action.OrganizeFiles or Action.OrganizeFolders or Action.FlattenOneLayer or Action.FlattenAllLayers).Count > 0)
                throw new IOException("Recover interrupted work before changing files in: " + folder);

        ConversionResult result;
        if (operation.IsConversion)
            result = await new SelectionConversionService(recovery).RunAsync(new(operation.Id, inputs, request.Options, request.OverwritePolicy)
                { ValidateBeforeConversion = request.ValidateBeforeConversion, OutputFolder = request.OutputFolder }, progress, token);
        else if (operation.Id == Action.TagKaraoke)
        {
            var service = new KaraokeTaggingService(recovery);
            var preview = await service.PreviewAsync(inputs, request.TaggingPattern, request.Options.RecurseFolders, token);
            var applied = await service.ApplyAsync(preview.Where(p => p.CanApply).ToArray(), request.Tagging, request.Options, progress, token);
            var rejected = preview.Where(p => !p.CanApply).Select(p => new ConversionFileResult(p.Path,
                Path.GetExtension(p.Path).ToLowerInvariant() is ".bin" or ".cdg" ? ConversionStatus.Skipped : ConversionStatus.Failed, [], p.Error ?? "Cannot tag this input."));
            result = applied with { Files = applied.Files.Concat(rejected).ToArray(), Found = preview.Count };
        }
        else if (operation.Id is Action.OrganizeFiles or Action.OrganizeFolders)
            result = await new AlphabeticalOrganizer().RunAsync(inputs,
                operation.Id == Action.OrganizeFiles ? AlphabeticalOrganizeScope.Files : AlphabeticalOrganizeScope.Folders, progress, token, request.IgnoreLeadingThe);
        else if (operation.Id is Action.FlattenOneLayer or Action.FlattenAllLayers)
            result = await new FlattenFolderLayer().RunAsync(inputs, progress, token,
                operation.Id == Action.FlattenAllLayers ? FolderFlattenScope.AllLayers : FolderFlattenScope.OneLayer);
        else if (operation.Id == Action.RestoreQuarantined)
        {
            var restored = new List<ConversionFileResult>();
            foreach (string manifest in inputs)
            {
                token.ThrowIfCancellationRequested();
                try { await new QuarantineService().RestoreAsync(manifest); restored.Add(new(manifest, ConversionStatus.Succeeded, [], "Files restored from manifest.")); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                { restored.Add(new(manifest, ConversionStatus.Failed, [], error.Message)); }
            }
            result = new(restored, false) { Found = inputs.Length };
        }
        else throw new NotSupportedException("No unattended executor is registered for " + operation.Id);
        return FromConversion(operation.Identifier, result);
    }

    public IReadOnlyList<RecoveryJob> FindRecovery(IEnumerable<string> folders)
    {
        var selection = folders.ToArray();
        return selection.Length > 0
            ? selection.SelectMany(folder => recovery.Find(Path.GetFullPath(folder))).DistinctBy(j => j.JournalPath, InputSelection.Paths).ToArray()
            : recovery.Find();
    }

    public async Task<ExecutionResult> ResumeAsync(string journal, IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
    {
        string full = Path.GetFullPath(journal);
        string root = Path.GetDirectoryName(Path.GetDirectoryName(full))!;
        var job = recovery.Find(root).SingleOrDefault(j => InputSelection.Paths.Equals(j.JournalPath, full))
            ?? throw new ArgumentException("No available recovery job matches that journal. Use recovery-list first.");
        if (job.Problem != null) throw new IOException(job.Problem);
        if (job.Kind == RecoveryKind.Recipe)
        {
            var recipe = RecipeRunner.ReadRecoveryRecipe(job);
            RequireUnattendedRecipe(recipe);
            return FromRecipe(recipe.Name, await new RecipeRunner(recovery: recovery).ResumeAsync(job, progress, token));
        }
        var result = job.Kind == RecoveryKind.Tagging
            ? await new KaraokeTaggingService(recovery).ResumeAsync(job, progress, token)
            : await new ConversionService(recovery).ResumeAsync(job, progress, token);
        return FromConversion("Recovery — " + job.Action, result);
    }

    private static ExecutionResult FromConversion(string operation, ConversionResult result)
    {
        var report = CompletionReport.FromConversion(operation, result);
        if (operation == "TagKaraoke") report = report with { ReportType = "Tagging Report" };
        return new(report, result.Found, result.Failed > 0);
    }
}
