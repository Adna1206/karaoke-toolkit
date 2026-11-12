using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Validation;
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
        if (operation.Action is { } action)
            return await new SelectionConversionService(recovery).RunAsync(new(action, inputs.ToArray(), step.Options, step.OverwritePolicy)
                { ValidateBeforeConversion = step.ValidateBeforeConversion }, progress, token, resolveConflict);
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
/// transaction/recovery safeguards; this runner does not automatically resume whole recipes.</remarks>
public sealed class RecipeRunner(IRecipeStepExecutor? executor = null)
{
    private readonly IRecipeStepExecutor executor = executor ?? new RecipeStepExecutor();

    public async Task<RecipeRunResult> RunAsync(RecipeDefinition recipe, IEnumerable<string> inputs,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default, Func<string, string>? operationLabel = null)
    {
        RecipeOperations.Validate(recipe); // Reject the complete known-invalid pipeline before touching inputs.
        string[] current = RecipeOperations.NormalizeInputs(recipe, inputs);
        var watch = Stopwatch.StartNew();
        var results = new List<RecipeStepResult>();
        bool cancelled = false;
        string reason = "";
        for (int index = 0; index < recipe.Steps.Count; index++)
        {
            var step = recipe.Steps[index];
            var operation = RecipeOperations.Get(step.OperationId);
            try
            {
                token.ThrowIfCancellationRequested();
                if (current.Length == 0) { reason = $"Step {index + 1} has no outputs to process."; break; }
                if (operation.Inputs == RecipeInputKind.Folders && current.Any(path => !Directory.Exists(path)))
                    throw new ArgumentException("This step requires folder inputs.");
                // File-moving operations must not move an interrupted conversion's journal or sources.
                bool movesFiles = operation.Id is "OrganizeFiles" or "OrganizeFolders" or "FlattenOneLayer" or "FlattenAllLayers" or "Cleanup_Filenames";
                foreach (string folder in current.Select(path => Directory.Exists(path) ? path : Path.GetDirectoryName(path)!).Distinct(InputSelection.Paths))
                {
                    var interrupted = await Task.Run(() => new RecoveryStore().Find(folder, movesFiles || step.Options.RecurseFolders), token);
                    if (interrupted.Count > 0)
                        throw new IOException("Recover interrupted work before running a recipe in: " + folder);
                }
                int stepIndex = index;
                var reporter = new InlineProgress(p => progress?.Report(p with
                {
                    TotalPercent = (stepIndex * 100 + p.TotalPercent) / recipe.Steps.Count,
                    Message = $"Step {stepIndex + 1}/{recipe.Steps.Count} ({operationLabel?.Invoke(step.OperationId) ?? step.OperationId}): {p.Message}"
                }));
                var result = await executor.ExecuteAsync(step, current, reporter, token);
                results.Add(new(index + 1, step.OperationId, result));
                if (operation.Outputs != RecipeInputKind.None)
                    current = result.Files.Where(file => !file.IsMetadata && file.Status == ConversionStatus.Succeeded)
                        .SelectMany(file => operation.Id == "LosslessG_To_Mp3G" ? file.Outputs.Append(file.Source) : file.Outputs)
                        .Where(IsPrimaryOutput).Distinct(InputSelection.Paths).ToArray();
                else if (operation.Id == "OrganizeFiles")
                    current = current.SelectMany(path => Directory.Exists(path) ? new[] { path }
                        : result.Files.FirstOrDefault(file => InputSelection.Paths.Equals(file.Source, path)) is { Status: ConversionStatus.Succeeded } moved
                            ? moved.Outputs : new[] { path })
                        .Where(path => File.Exists(path) || Directory.Exists(path)).Distinct(InputSelection.Paths).ToArray();
                if (result.Cancelled) { cancelled = true; reason = $"Cancelled during step {index + 1}."; break; }
                if (result.Failed > 0) { reason = $"Stopped at step {index + 1}: {result.Failed} item(s) failed."; break; }
                if (result.Files.Count == 0) { reason = $"Step {index + 1} found no matching items."; break; }
            }
            catch (OperationCanceledException) { cancelled = true; reason = $"Cancelled before completing step {index + 1}."; break; }
            catch (Exception error)
            {
                results.Add(new(index + 1, step.OperationId, new([new(recipe.Name, ConversionStatus.Failed, [], error.Message)], false)));
                reason = $"Stopped at step {index + 1}: {error.Message}";
                break;
            }
        }
        return new(results, current, cancelled, reason, watch.Elapsed);
    }

    private static bool IsPrimaryOutput(string path) => File.Exists(path) &&
        InputSelection.Extensions.Concat([".mp4", ".mkv", ".avi"]).Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private sealed class InlineProgress(System.Action<ConversionProgress> report) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => report(value);
    }
}
