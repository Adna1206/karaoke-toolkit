using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Validation;
using KaraokeToolkit.Core.Tagging;
using System.Diagnostics;

namespace KaraokeToolkit.Core.Recipes;

public sealed record RecipeStepResult(int Number, string OperationId, ConversionResult Result);
public sealed record RecipeRunResult(IReadOnlyList<RecipeStepResult> Steps, IReadOnlyList<string> Outputs,
    bool Cancelled, string StopReason, TimeSpan Elapsed)
{
    public CompletionReport ToReport(string name, Func<string, string>? operationLabel = null) => new(name, DateTimeOffset.Now, Cancelled,
        Steps.SelectMany(step => step.Result.Files.Select(file => new ReportEntry(file.Source, file.Status.ToString(),
            $"Step {step.Number} ({operationLabel?.Invoke(step.OperationId) ?? step.OperationId}): {file.Message}", file.Outputs)))
        .Concat(string.IsNullOrEmpty(StopReason) ? [] : new[] { new ReportEntry(name, Cancelled ? "Cancelled" : "Stopped", StopReason, []) }).ToArray())
        { ReportType = "Recipe Report" };
}

/// <summary>Runs one existing operation. This boundary also allows deterministic orchestration tests.</summary>
public interface IRecipeStepExecutor
{
    Task<ConversionResult> ExecuteAsync(RecipeStep step, IReadOnlyList<string> inputs,
        IProgress<ConversionProgress>? progress, CancellationToken token);
}

public sealed class RecipeStepExecutor(RecoveryStore? recovery = null,
    Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict = null) : IRecipeStepExecutor
{
    public async Task<ConversionResult> ExecuteAsync(RecipeStep step, IReadOnlyList<string> inputs,
        IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        var operation = RecipeOperations.Get(step.OperationId);
        if (WorkflowSession.Current.Value is { Data.Moves: not null } workflow)
            return await Task.Run(() => WorkflowMoves.Run(workflow, null, progress, token), token);
        if (operation.Action is { } action)
            return await new SelectionConversionService(recovery).RunAsync(new(action, inputs.ToArray(), step.Options, step.OverwritePolicy)
                { ValidateBeforeConversion = step.ValidateBeforeConversion }, progress, token, resolveConflict);
        if (operation.Id == "TagKaraoke")
            return await new KaraokeTaggingService(recovery).ApplyRecipeStepAsync(inputs, step.TaggingPattern, step.Tagging, step.Options, progress, token);
        if (operation.Id == "ValidateKaraoke")
        {
            var result = await new ValidationService().ScanAsync(inputs, step.Options.RecurseFolders, progress, token);
            return new(result.Findings.Select(finding => new ConversionFileResult(finding.Source,
                finding.Status == ValidationStatus.Valid ? ConversionStatus.Succeeded : ConversionStatus.Failed,
                [], finding.Message)).ToArray(), result.Cancelled) { Found = result.Findings.Count, Elapsed = result.Elapsed };
        }
        if (operation.Id.StartsWith("Organize", StringComparison.Ordinal))
            return await new AlphabeticalOrganizer().RunAsync(inputs,
                operation.Id == "OrganizeFiles" ? AlphabeticalOrganizeScope.Files : AlphabeticalOrganizeScope.Folders,
                progress, token, step.IgnoreLeadingThe);
        return await new FlattenFolderLayer().RunAsync(inputs, progress, token,
            operation.Id == "FlattenAllLayers" ? FolderFlattenScope.AllLayers : FolderFlattenScope.OneLayer);
    }
}

/// <summary>Feeds each step only the previous step's primary outputs. Stops on failure or cancellation.</summary>
/// <remarks>Folder maintenance retains the selected root scope. Each conversion keeps its existing
/// transaction/recovery safeguards; the parent checkpoint continues the saved pipeline after a crash.</remarks>
public sealed class RecipeRunner(IRecipeStepExecutor? executor = null, RecoveryStore? recovery = null)
{
    private readonly IRecipeStepExecutor executor = executor ?? new RecipeStepExecutor(recovery);
    private readonly RecoveryStore recovery = recovery ?? new();
    public static RecipeDefinition ReadRecoveryRecipe(RecoveryJob job)
    {
        var data = WorkflowSession.Read(job.JournalPath);
        if (data.Kind != RecoveryKind.Recipe) throw new IOException("This recovery job is not a recipe.");
        return data.Recipe!;
    }

    /// <summary>Validates the pipeline and checkpoints its input scope before running steps in sequence.</summary>
    /// <remarks>Failure or cancellation stops later steps without undoing completed changes.
    /// Primary outputs feed the next step; folder maintenance retains the selected root scope.</remarks>
    public Task<RecipeRunResult> RunAsync(RecipeDefinition recipe, IEnumerable<string> inputs,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default, Func<string, string>? operationLabel = null)
        => Task.Run(async () =>
    {
        RecipeOperations.Validate(recipe); // Reject the complete known-invalid pipeline before touching inputs.
        string[] current = RecipeOperations.NormalizeInputs(recipe, inputs);
        foreach (string root in WorkflowSession.RootsFor(current))
            if (this.recovery.Find(root).Count > 0) throw new IOException("Recover interrupted work before starting this recipe: " + root);
        using var session = new WorkflowSession(this.recovery, new WorkflowData
        { Kind = RecoveryKind.Recipe, Roots = WorkflowSession.RootsFor(current), Inputs = current, CurrentInputs = current, Recipe = recipe });
        return await RunCoreAsync(session, progress, token, operationLabel);
    });

    /// <summary>Continues the saved pipeline and input plan, reconciling interrupted work before later steps.</summary>
    /// <remarks>The persisted recipe is authoritative; a current recipe-library entry is not required.</remarks>
    public Task<RecipeRunResult> ResumeAsync(RecoveryJob job, IProgress<ConversionProgress>? progress = null,
        CancellationToken token = default, Func<string, string>? operationLabel = null)
        => Task.Run(async () =>
        {
            using var session = new WorkflowSession(recovery, job);
            if (session.Data.Kind != RecoveryKind.Recipe) throw new IOException("This recovery job is not a recipe.");
            session.Enter();
            session.RecoverTransaction();
            session.VerifyExpected();
            return await RunCoreAsync(session, progress, token, operationLabel);
        });

    private async Task<RecipeRunResult> RunCoreAsync(WorkflowSession session, IProgress<ConversionProgress>? progress,
        CancellationToken token, Func<string, string>? operationLabel)
    {
        session.Enter();
        var recipe = session.Data.Recipe!;
        string[] current = session.Data.CurrentInputs;
        var watch = Stopwatch.StartNew();
        var results = session.Data.Steps.ToList();
        bool cancelled = results.LastOrDefault()?.Result.Cancelled ?? false;
        string reason = results.LastOrDefault() is { } last ? StepStopReason(last) : "";
        for (int index = results.Count; index < recipe.Steps.Count && reason.Length == 0; index++)
        {
            var step = recipe.Steps[index];
            var operation = RecipeOperations.Get(step.OperationId);
            ConversionResult result;
            try
            {
                token.ThrowIfCancellationRequested();
                if (current.Length == 0) { reason = $"Step {index + 1} has no outputs to process."; break; }
                if (operation.Inputs == RecipeInputKind.Folders && current.Any(path => !Directory.Exists(path)))
                    throw new ArgumentException("This step requires folder inputs.");
                if (!session.Data.StepStarted) session.StartStep();
                // File-moving operations must not move an interrupted conversion's journal or sources.
                bool movesFiles = operation.Id is "OrganizeFiles" or "OrganizeFolders" or "FlattenOneLayer" or "FlattenAllLayers" or "CleanUpFilenames";
                foreach (string folder in current.Select(path => Directory.Exists(path) ? path : Path.GetDirectoryName(path)!).Distinct(InputSelection.Paths))
                {
                    token.ThrowIfCancellationRequested();
                    var interrupted = recovery.Find(folder, movesFiles || step.Options.RecurseFolders);
                    if (interrupted.Count > 0)
                        throw new IOException("Recover interrupted work before running a recipe in: " + folder);
                }
                int stepIndex = index;
                string prefix = $"Step {stepIndex + 1}/{recipe.Steps.Count} ({operationLabel?.Invoke(step.OperationId) ?? step.OperationId}): ";
                var reporter = new InlineProgress(p => progress?.Report(p with
                {
                    TotalPercent = (stepIndex * 100 + p.TotalPercent) / recipe.Steps.Count,
                    Message = prefix + p.Message,
                    ActivityLabel = p.ActivityLabel == null ? null : prefix + p.ActivityLabel
                }));
                result = await executor.ExecuteAsync(step, current, reporter, token);
            }
            catch (OperationCanceledException) { cancelled = true; reason = $"Cancelled before completing step {index + 1}."; break; }
            catch (Exception error)
            {
                results.Add(new(index + 1, step.OperationId, new([new(recipe.Name, ConversionStatus.Failed, [], error.Message)], false)));
                reason = $"Stopped at step {index + 1}: {error.Message}";
                break;
            }
            var completed = new RecipeStepResult(index + 1, step.OperationId, result);
            try
            {
                var next = SelectNextInputs(current, operation.Outputs, operation.Id, result);
                session.CompleteStep(completed, next);
                current = next;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Could not save the checkpoint for recipe step {completed.Number}. Use Recover to resume this workflow. {error.Message}", error);
            }
            results.Add(completed);
            cancelled = result.Cancelled;
            reason = StepStopReason(completed);
            progress?.Report(new(100, (index + 1) * 100 / recipe.Steps.Count, result.Found, result.Found,
                $"Completed step {index + 1}/{recipe.Steps.Count}.", watch.Elapsed.ToString(@"hh\:mm\:ss")));
        }
        session.Finish(); // An explicit stop/cancel is not a crash; unresolved publications keep their checkpoints.
        return new(results, current, cancelled, reason, watch.Elapsed);
    }

    private static string StepStopReason(RecipeStepResult step) => step.Result switch
    {
        { Cancelled: true } => $"Cancelled during step {step.Number}.",
        { Failed: > 0 } => $"Stopped at step {step.Number}: {step.Result.Failed} item(s) failed.",
        { Files.Count: 0 } => $"Step {step.Number} found no matching items.",
        _ => ""
    };

    internal static string[] SelectNextInputs(string[] current, RecipeInputKind outputs, string operationId, ConversionResult result)
    {
        if (outputs != RecipeInputKind.None)
            return result.Files.Where(file => !file.IsMetadata && file.Status == ConversionStatus.Succeeded)
                .SelectMany(file => operationId == "LosslessGToMp3G" ? file.Outputs.Append(file.Source) : file.Outputs)
                .Where(IsPrimaryOutput).Distinct(InputSelection.Paths).ToArray();
        if (operationId != "OrganizeFiles") return current;

        var bySource = new Dictionary<string, ConversionFileResult>(InputSelection.Paths);
        foreach (var file in result.Files) bySource.TryAdd(file.Source, file);
        return current.SelectMany(path => !Directory.Exists(path) &&
            bySource.GetValueOrDefault(path) is { Status: ConversionStatus.Succeeded } moved ? moved.Outputs : new[] { path })
            .Where(path => File.Exists(path) || Directory.Exists(path)).Distinct(InputSelection.Paths).ToArray();
    }

    private static bool IsPrimaryOutput(string path) => File.Exists(path) &&
        InputSelection.Extensions.Concat([".mp4", ".mkv", ".avi"]).Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private sealed class InlineProgress(System.Action<ConversionProgress> report) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => report(value);
    }
}
