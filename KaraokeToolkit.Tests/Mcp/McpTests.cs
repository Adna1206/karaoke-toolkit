using System.Text.Json;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KaraokeToolkit.Tests.Mcp;

public class McpTests
{
    private sealed class FakeExecutor : IOperationExecutor
    {
        internal int Calls;
        public Task<ExecutionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken token)
        {
            Calls++; token.ThrowIfCancellationRequested();
            progress?.Report(new(20, 20, 1, 0, "First", "0")); progress?.Report(new(10, 10, 1, 0, "Second", "0"));
            return Task.FromResult(new ExecutionResult(new("Test", DateTimeOffset.Now, false, []), 1, false));
        }
    }
    private sealed class ProgressSink : IProgress<ProgressNotificationValue>
    {
        internal System.Collections.Concurrent.ConcurrentQueue<ProgressNotificationValue> Values = new();
        public void Report(ProgressNotificationValue value) => Values.Enqueue(value);
    }

    [Fact]
    public void EveryCoreOperationHasAnExplicitMcpDecision()
    {
        Assert.Equal(Enum.GetValues<OperationId>().Order(), McpOperationCatalog.All.Select(r => r.Id).Order());
        foreach (var registration in McpOperationCatalog.All)
        {
            if (registration.Included) { Assert.Null(registration.ExclusionReason); Assert.True(OperationCatalog.Get(registration.Id).CanRunUnattended); }
            else { Assert.False(string.IsNullOrWhiteSpace(registration.ExclusionReason)); Assert.Throws<NotSupportedException>(() => McpOperationCatalog.RequireIncluded(registration.Id)); }
        }
        Assert.False(McpOperationCatalog.Get(OperationId.DiscRip).Included);
        Assert.Throws<ArgumentOutOfRangeException>(() => McpOperationCatalog.Get((OperationId)999));
        Assert.DoesNotContain(typeof(McpTools).Assembly.GetReferencedAssemblies(), a => a.Name is "KaraokeToolkit.Cli" || a.Name!.StartsWith("Avalonia"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task NeitherToolArgumentsNorStartupPermissionAloneCanEnableWrites(bool enabled, bool confirmed)
    {
        var executor = new FakeExecutor();
        using var adapter = new McpOperationsService(new(enabled), executor, new());
        await Assert.ThrowsAsync<McpException>(() => adapter.RunAsync(new(OperationId.AudioGToZip, ["unused"]), confirmed, null, default));
        Assert.Equal(0, executor.Calls);
    }

    [Fact]
    public async Task ReadOnlyCallsWorkAndProgressIsMonotonic()
    {
        var executor = new FakeExecutor(); var progress = new ProgressSink();
        using var adapter = new McpOperationsService(new(false), executor, new());
        await adapter.RunAsync(new(OperationId.ValidateKaraoke, ["unused"]), false, progress, default);
        Assert.Equal(new float[] { 20, 20 }, progress.Values.Select(v => v.Progress));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.RunAsync(new(OperationId.ValidateKaraoke, ["unused"]), false, null, cancelled.Token));
        Assert.Equal(1, executor.Calls);
    }

    private static async Task<McpClient> Connect(bool writes = false)
    {
        var transport = new StdioClientTransport(new()
        {
            Name = "Karaoke Toolkit test",
            Command = "dotnet",
            Arguments = writes ? [typeof(McpTools).Assembly.Location, "--allow-writes"] : [typeof(McpTools).Assembly.Location]
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
    }
    private static JsonDocument Json(CallToolResult result) => JsonDocument.Parse(result.Content.OfType<TextContentBlock>().First().Text);

    [Fact]
    public async Task StdioClientDiscoversSchemasAndReadOnlyModeCannotWrite()
    {
        await using var client = await Connect();
        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "run_operation"); Assert.Contains(tools, t => t.Name == "get_settings");
        var schema = tools.Single(t => t.Name == "run_operation").JsonSchema.GetRawText();
        Assert.Contains("request", schema); Assert.Contains("confirmFileChanges", schema);
        using var operations = Json(await client.CallToolAsync("get_operations"));
        Assert.False(operations.RootElement.GetProperty("allowFileChanges").GetBoolean());
        Assert.Equal(Enum.GetValues<OperationId>().Length, operations.RootElement.GetProperty("operations").GetArrayLength());
        using var settings = Json(await client.CallToolAsync("get_settings"));
        Assert.Equal(320, settings.RootElement.GetProperty("defaults").GetProperty("options").GetProperty("audioBitrate").GetInt32());
        string root = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(root, OperationId.ZipToBin, "keep");
        var result = await client.CallToolAsync("run_operation", new Dictionary<string, object?>
        { ["request"] = new { operationId = "ZipToAudioG", inputs = new[] { root } }, ["confirmFileChanges"] = true });
        Assert.True(result.IsError); Assert.False(File.Exists(Path.Combine(root, "song.wav")));
        var check = await client.CallToolAsync("validate_karaoke", new Dictionary<string, object?> { ["inputs"] = new[] { root } });
        Assert.False(check.IsError ?? false); using var report = Json(check);
        Assert.Equal("Validation Report", report.RootElement.GetProperty("report").GetProperty("reportType").GetString());
    }

    [Fact]
    public async Task StdioProcessingKeepsOriginalsAndReturnsStructuredReport()
    {
        await using var client = await Connect(true);
        string root = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(root, OperationId.ZipToBin, "keep");
        var request = new { operationId = "ZipToAudioG", inputs = new[] { root }, options = new { AutoCreateCueText = false, DeleteOldFiles = false } };
        var plan = await client.CallToolAsync("plan_operation", new Dictionary<string, object?> { ["request"] = request });
        Assert.False(plan.IsError ?? false); Assert.False(File.Exists(Path.Combine(root, "song.wav")));
        var denied = await client.CallToolAsync("run_operation", new Dictionary<string, object?> { ["request"] = request });
        Assert.True(denied.IsError);
        var wireProgress = new ProgressSink();
        var result = await client.CallToolAsync("run_operation", new Dictionary<string, object?> { ["request"] = request, ["confirmFileChanges"] = true }, progress: wireProgress);
        Assert.False(result.IsError ?? false); Assert.True(File.Exists(Path.Combine(root, "song.wav"))); Assert.True(File.Exists(Path.Combine(root, "song.zip")));
        Assert.NotNull(result.StructuredContent); using var json = Json(result);
        Assert.Equal(1, json.RootElement.GetProperty("found").GetInt32());
        Assert.NotEmpty(wireProgress.Values);
        Assert.Equal(wireProgress.Values.Select(p => p.Progress).Order(), wireProgress.Values.Select(p => p.Progress));
    }

    [Fact]
    public async Task StdioRecoversSavedConversionSettings()
    {
        string root = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(root, OperationId.ZipToBin, "keep"); string journal;
        using (var run = new RunSession(new(TestMediaFixture.NewFolder()), new(OperationId.ZipToAudioG, root,
            new() { DeleteOldFiles = false, AutoCreateCueText = false }), OverwritePolicy.Fail, [Path.Combine(root, "song.zip")])) journal = run.Journal;
        await using var client = await Connect(true);
        var listing = await client.CallToolAsync("list_recovery", new Dictionary<string, object?> { ["folders"] = new[] { root } });
        Assert.False(listing.IsError ?? false);
        var result = await client.CallToolAsync("recover", new Dictionary<string, object?> { ["journal"] = journal, ["confirmFileChanges"] = true });
        Assert.False(result.IsError ?? false); Assert.True(File.Exists(Path.Combine(root, "song.wav")));
        Assert.True(File.Exists(Path.Combine(root, "song.zip"))); Assert.False(File.Exists(journal));
    }

    [Fact]
    public async Task StdioRecipeUsesSavedStepsAndSettings()
    {
        await using var client = await Connect(true);
        string root = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(root, OperationId.ZipToBin, "keep");
        var recipe = new
        {
            name = "MCP extraction",
            steps = new object[]
            {
                new { operationId = "ZipToAudioG", overwritePolicy = "Fail", options = new { autoCreateCueText = false } },
                new { operationId = "ValidateKaraoke", overwritePolicy = "Fail" }
            }
        };
        var result = await client.CallToolAsync("run_recipe", new Dictionary<string, object?>
        { ["recipe"] = recipe, ["inputs"] = new[] { root }, ["confirmFileChanges"] = true });
        Assert.False(result.IsError ?? false); using var report = Json(result);
        Assert.Equal("Recipe Report", report.RootElement.GetProperty("report").GetProperty("reportType").GetString());
        Assert.True(File.Exists(Path.Combine(root, "song.zip"))); Assert.True(File.Exists(Path.Combine(root, "song.wav")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"audioBitrate\":192}")]
    [InlineData("{\"AudioBitrate\":192}")]
    public void OmittedDeletionPreferenceAlwaysKeepsOriginals(string json)
        => Assert.False(JsonSerializer.Deserialize<ConversionOptions>(json, Program.JsonOptions)!.DeleteOldFiles);

    [Theory]
    [InlineData("{\"audioBitrate\":192,\"AudioBitrate\":320}")]
    [InlineData("{\"audioEncoder\":0}")]
    [InlineData("{\"unknownSetting\":true}")]
    public void SettingsRejectUnknownDuplicateAndNumericEnums(string json)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConversionOptions>(json, Program.JsonOptions));

    [Fact]
    public async Task ExcludedOperationsCannotBypassExposureThroughRecovery()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "run.json");
        File.WriteAllText(path, """{"Request":{"Action":"DiscRip"}}""");
        var executor = new FakeExecutor(); using var adapter = new McpOperationsService(new(true), executor, new());
        var error = await Assert.ThrowsAsync<McpException>(() => adapter.ResumeAsync(path, true, null, default));
        Assert.Contains("not exposed", error.Message); Assert.Equal(0, executor.Calls); Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task UnknownOperationIsAnActionableMcpToolError()
    {
        await using var client = await Connect();
        var result = await client.CallToolAsync("run_operation", new Dictionary<string, object?>
        { ["request"] = new { operationId = "FutureUnknownOperation", inputs = new[] { "unused" } } });
        Assert.True(result.IsError);
        string message = result.Content.OfType<TextContentBlock>().First().Text;
        Assert.Contains("Unregistered MCP operation", message);
        Assert.Contains("FutureUnknownOperation", message);
        Assert.Contains("get_operations", message);
    }

    [Fact]
    public async Task ExcludedOperationReturnsItsReasonToTheAiClient()
    {
        await using var client = await Connect();
        var result = await client.CallToolAsync("run_operation", new Dictionary<string, object?>
        { ["request"] = new { operationId = "PlayAudioG", inputs = new[] { "unused" } } });
        Assert.True(result.IsError);
        string message = result.Content.OfType<TextContentBlock>().First().Text;
        Assert.Contains("PlayAudioG", message); Assert.Contains("excluded", message);
        Assert.Contains("Playback is deferred", message); Assert.Contains("get_operations", message);
    }
}
