using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.App.Features.Main;

public partial class MainViewModel
{
    public string RecipeDirectory { get; }
    public IReadOnlyList<ActionChoice> RecipeChoices { get; private set; } = [];

    /// <summary>Refreshes home recipes after library edits, preserving selection by stable recipe ID.</summary>
    public void ReloadRecipes()
    {
        try
        {
            var selectedId = SelectedOperation?.Recipe?.Id;
            var favouriteIds = Operations.Concat(RecipeChoices).Where(choice => choice.IsFavourite).Select(choice => choice.Id).ToHashSet(StringComparer.Ordinal);
            var recipes = new RecipeStore(RecipeDirectory).Load();
            // A broken app settings file must not hide valid recipes or replace its existing error.
            if (recipes.Count > 0)
                try { favouriteIds.UnionWith(store.LoadFavouriteOperations()); }
                catch (Exception error) when (error is System.Xml.XmlException or System.IO.IOException or UnauthorizedAccessException) { }
            RecipeChoices = recipes.Select(recipe =>
            {
                string summary = $"{recipe.Steps.Count} {(recipe.Steps.Count == 1 ? "step" : "steps")}: " + string.Join(" → ", recipe.Steps.Select(step => OperationDefinitions.All.Single(operation => operation.Id == step.OperationId).Label));
                var first = RecipeOperations.Get(recipe.Steps[0].OperationId);
                var definition = new OperationDefinition("Recipe:" + recipe.Id.ToString("D"), null, recipe.Name, summary,
                    summary + ". Uses this recipe's saved settings. Experimental: completed changes remain if a later step fails.",
                    OperationSection.Recipes, first.Inputs == RecipeInputKind.Folders ? OperationInputKind.Folders : OperationInputKind.FilesAndFolders, true);
                var choice = new ActionChoice(definition, recipe) { IsFavourite = favouriteIds.Contains(definition.Id) };
                choice.PropertyChanged += (_, change) =>
                {
                    if (change.PropertyName != nameof(ActionChoice.IsFavourite)) return;
                    OnPropertyChanged(nameof(FavouriteOperations)); RefreshNavigation(); SaveOperationSections();
                };
                return choice;
            }).ToArray();
            OnPropertyChanged(nameof(RecipeChoices));
            OnPropertyChanged(nameof(FavouriteOperations));
            if (selectedId != null)
                SelectedOperation = RecipeChoices.FirstOrDefault(choice => choice.Recipe!.Id == selectedId)
                    ?? Operations.First(operation => operation.Available);
            RefreshNavigation();
        }
        catch (Exception error) { Status = "Could not refresh saved recipes: " + error.Message; RefreshNavigation(); }
    }

    public async Task RunRecipeAsync(RecipeDefinition recipe, IEnumerable<string> inputs)
        => await RunRecipeCoreAsync(recipe, inputs, null);

    private async Task ResumeRecipeAsync(RecoveryJob job)
        => await RunRecipeCoreAsync(RecipeRunner.ReadRecoveryRecipe(job), [job.Folder], job);

    private async Task RunRecipeCoreAsync(RecipeDefinition recipe, IEnumerable<string> inputs, RecoveryJob? job)
    {
        if (IsBusy) return;
        RecipeOperations.Validate(recipe);
        var launchInputs = inputs.ToArray();
        if (job == null)
            foreach (string input in launchInputs)
                if (await CheckForRecoveryAsync(System.IO.Directory.Exists(input) ? input : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(input))!)) return;
        settingInputs = true;
        try { FolderPath = launchInputs.FirstOrDefault() ?? ""; selectedInputs = launchInputs; }
        finally { settingInputs = false; }
        InputSummary = string.Join("; ", launchInputs);
        var run = BeginRun((job == null ? "Recipe — " : "Recovery — ") + recipe.Name, "Starting recipe…");
        IsRecoveryMode = job != null;
        RecoveryMessage = job == null ? "" : "Recovery mode — resuming the saved recipe and its remaining steps.";
        try
        {
            string Label(string operationId) => OperationDefinitions.All.Single(operation => operation.Id == operationId).Label;
            var runner = new RecipeRunner(new RecipeStepExecutor(recovery, ResolveConflictAsync), recovery);
            var result = job == null ? await runner.RunAsync(recipe, launchInputs, run.Progress, run.Token, Label)
                : await runner.ResumeAsync(job, run.Progress, run.Token, Label);
            run.StopProgress();
            LastReport = result.ToReport(recipe.Name, Label);
            FoundFiles = result.Steps.Sum(step => step.Result.Found);
            Elapsed = result.Elapsed.ToString(@"hh\:mm\:ss");
            int failed = result.Steps.Sum(step => step.Result.Failed);
            StatusOutcome = RunStatusSummary.From(result.Cancelled,
                result.Steps.Sum(step => step.Result.Succeeded), failed, result.Steps.Sum(step => step.Result.Skipped));
            if (!result.Cancelled && failed == 0 && result.StopReason.Length > 0) StatusOutcome = RunStatus.Warning;
            Status = result.Cancelled ? "Recipe cancelled. Completed changes remain."
                : result.StopReason.Length > 0 ? result.StopReason : $"Recipe finished: {result.Steps.Count} steps completed.";
            if (!result.Cancelled && result.StopReason.Length == 0) Progress = TotalProgress = 100;
            foreach (var step in result.Steps)
                AppendActivityIssues(step.Result.Files, $"Step {step.Number}/{recipe.Steps.Count} ({Label(step.OperationId)}): ");
            Activity.Add(new(Elapsed, Status));
        }
        catch (Exception error)
        {
            StatusOutcome = RunStatus.Failure; Status = "Recipe failed: " + error.Message;
            LastReport = new(recipe.Name, DateTimeOffset.Now, false, [new(recipe.Name, "Failed", error.Message, [])]) { ReportType = "Recipe Report" };
            Activity.Add(new(Elapsed, Status));
        }
        finally
        {
            EndRun(run);
            try
            {
                RecoveryAvailable = recovery.Find().Count > 0;
                RecoveryMessage = RecoveryAvailable ? "An interrupted workflow remains. Use Recover to review it." : "";
            }
            catch { /* Keep the recipe outcome visible. */ }
        }
    }
}
