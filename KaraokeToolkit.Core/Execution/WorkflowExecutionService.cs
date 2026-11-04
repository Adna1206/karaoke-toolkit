using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Validation;

namespace KaraokeToolkit.Core.Execution;

public sealed partial class OperationExecutionService
{
    public static void RequireUnattendedRecipe(RecipeDefinition recipe)
    {
        RecipeOperations.Validate(recipe);
        if (recipe.Steps.Any(s => s.OverwritePolicy == OverwritePolicy.Ask))
            throw new ArgumentException("Recipe steps must use fail, skip or replace; CLI recipes cannot prompt.");
    }

    public async Task<ExecutionResult> ExecuteRecipeAsync(RecipeDefinition recipe, IEnumerable<string> inputs,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
    {
        RequireUnattendedRecipe(recipe);
        return FromRecipe(recipe.Name, await new RecipeRunner(recovery: recovery).RunAsync(recipe, inputs, progress, token));
    }

    private static ExecutionResult FromRecipe(string name, RecipeRunResult result) => new(result.ToReport(name),
        result.Steps.FirstOrDefault()?.Result.Found ?? 0,
        !result.Cancelled && (result.Steps.Any(s => s.Result.Failed > 0) || !string.IsNullOrEmpty(result.StopReason)));

    public async Task<ExecutionResult> QuarantineAsync(IEnumerable<string> inputs, bool recurse,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default, bool dryRun = false)
    {
        var paths = inputs.Select(Path.GetFullPath).ToArray();
        foreach (string path in paths)
            if (recovery.Find(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!, true).Count > 0)
                throw new IOException("Recover interrupted work before quarantining files.");
        var validation = await new ValidationService().ScanAsync(paths, recurse, progress, token);
        if (validation.Cancelled) throw new OperationCanceledException(token);
        if (dryRun) return new(new("Quarantine Invalid Karaoke", DateTimeOffset.Now, false,
            validation.Findings.Select(f => new ReportEntry(f.Source, f.CanMove ? "Planned" : "Skipped", f.Message,
                f.CanMove ? [Path.Combine(f.Root, "Damaged", "<batch>", "manifest.json")] : [])).ToArray()) { ReportType = "Dry Run Report" }, validation.Findings.Count, false);
        var moved = await new QuarantineService().MoveAsync(validation.Findings.Where(f => f.CanMove), token);
        return new(new("Quarantine Invalid Karaoke", DateTimeOffset.Now, false,
            moved.Select(f => new ReportEntry(f.Source, f.Succeeded ? "Succeeded" : "Failed", f.Message,
                f.Manifest == null ? [] : f.MovedFiles.Append(f.Manifest).ToArray())).ToArray()) { ReportType = "Quarantine Report" },
            validation.Findings.Count, moved.Any(f => !f.Succeeded) || validation.Findings.Any(f => f.Status != ValidationStatus.Valid && !f.CanMove));
    }
}
