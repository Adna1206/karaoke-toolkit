using System.Text.Json;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Recovery;
using ModelContextProtocol;

namespace KaraokeToolkit.Mcp;

/// <summary>MCP adapter and access checks only; all processing/recovery stays in Core.</summary>
public sealed class McpOperationsService(McpAccessPolicy policy, IOperationExecutor executor, OperationExecutionService workflows) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public Task<ExecutionResult> RunAsync(OperationRequest request, bool confirmed, IProgress<ProgressNotificationValue>? progress, CancellationToken token)
        => Guard(async () =>
        {
            if (McpOperationCatalog.RequireIncluded(request.OperationId).ChangesFiles) policy.RequireFileChanges(confirmed);
            return await executor.ExecuteAsync(request, Reporter(progress), token);
        }, token);

    public Task<ExecutionResult> PlanAsync(OperationRequest request, CancellationToken token) => Guard(async () =>
    {
        McpOperationCatalog.RequireIncluded(request.OperationId);
        return await new OperationPlanner().PlanAsync(request, token);
    }, token);

    public Task<ExecutionResult> RecipeAsync(RecipeDefinition recipe, string[] inputs, bool confirmed, IProgress<ProgressNotificationValue>? progress, CancellationToken token)
        => Guard(async () =>
        {
            policy.RequireFileChanges(confirmed); CheckRecipe(recipe);
            return await workflows.ExecuteRecipeAsync(recipe, inputs, Reporter(progress), token);
        }, token);

    public IReadOnlyList<RecoveryJob> ListRecovery(string[] folders) => workflows.FindRecovery(folders);

    public Task<ExecutionResult> ResumeAsync(string journal, bool confirmed, IProgress<ProgressNotificationValue>? progress, CancellationToken token)
        => Guard(async () =>
        {
            policy.RequireFileChanges(confirmed);
            // Apply exposure rules to saved identities as well, rather than bypassing
            // the catalogue through recovery. Core still validates all journal paths/hashes.
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(journal, token));
            CheckSavedIdentities(json.RootElement);
            return await workflows.ResumeAsync(journal, Reporter(progress), token);
        }, token);

    private static void CheckSavedIdentities(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name is "Action" or "OperationId")
                {
                    if (property.Value.ValueKind != JsonValueKind.String) throw new ArgumentException("Saved operations must be named.");
                    McpOperationCatalog.RequireIncluded(OperationCatalog.Get(property.Value.GetString()!).Id);
                }
                else CheckSavedIdentities(property.Value);
            }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CheckSavedIdentities(item);
    }

    private static void CheckRecipe(RecipeDefinition recipe)
    {
        OperationExecutionService.RequireUnattendedRecipe(recipe);
        foreach (var step in recipe.Steps) McpOperationCatalog.RequireIncluded(OperationCatalog.Get(step.OperationId).Id);
    }

    private async Task<ExecutionResult> Guard(Func<Task<ExecutionResult>> action, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { token.ThrowIfCancellationRequested(); return await action(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        { throw new McpException(error.Message, error); }
        finally { gate.Release(); }
    }

    private static IProgress<ConversionProgress>? Reporter(IProgress<ProgressNotificationValue>? progress) => progress == null ? null : new ProgressAdapter(progress);
    private sealed class ProgressAdapter(IProgress<ProgressNotificationValue> sink) : IProgress<ConversionProgress>
    {
        private int last;
        public void Report(ConversionProgress value)
        {
            lock (this) { last = Math.Max(last, Math.Clamp(value.TotalPercent, 0, 100)); sink.Report(new() { Progress = last, Total = 100, Message = value.Message }); }
        }
    }
    public void Dispose() => gate.Dispose();
}
