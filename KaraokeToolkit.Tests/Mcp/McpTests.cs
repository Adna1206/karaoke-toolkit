using System.Text.Json;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KaraokeToolkit.Tests.Mcp;

/// <summary>Checks MCP exposure and write policy with fakes, plus schemas, reports and recovery through real stdio children.</summary>
public class McpTests
{
    [Fact]
    public async Task SharedSettingsWritesRequirePermissionAndRunOverridesWin()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "options.xmls");
        File.WriteAllText(path, "<KaraokeToolkit schemaVersion='1'><Processing><Setting id='recovery.verification'>Sha256</Setting></Processing></KaraokeToolkit>");
        var profile = new SharedSettingsStore(path);
        var blocked = new McpAccessPolicy(false);
        using var reader = new McpOperationsService(blocked, new FakeExecutor(), new());
        var readOnly = new McpTools(reader, blocked, profile);
        var writable = new McpAccessPolicy(true);
        var executor = new FakeExecutor();
        using var writer = new McpOperationsService(writable, executor, new());
        var tools = new McpTools(writer, writable, profile);
        var sha = JsonSerializer.SerializeToElement("Sha256");

        Assert.Equal(RecoveryVerificationMode.Sha256, readOnly.GetSetting("recovery.verification").Value);
        string original = File.ReadAllText(path);
        Assert.Throws<McpException>(() => readOnly.SetSetting("recovery.verification", sha, true));
        Assert.Throws<McpException>(() => tools.SetSetting("recovery.verification", sha));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Throws<McpException>(() => tools.SetSetting("files.recurse", JsonSerializer.SerializeToElement("false"), true));
        tools.SetSetting("recovery.verification", sha, true);
        tools.SetSetting("files.recurse", JsonSerializer.SerializeToElement(false), true);
        tools.SetSetting("recovery.check-on-startup", JsonSerializer.SerializeToElement(false), true);
        tools.SetSetting("recovery.batch-resume", JsonSerializer.SerializeToElement(false), true);
        tools.SetSetting("recovery.check-timing", JsonSerializer.SerializeToElement("BeforeItem"), true);
        Assert.False((bool)readOnly.GetSetting("recovery.check-on-startup").Value);

        var sink = new ProgressSink();
        await tools.RunOperation(JsonSerializer.Deserialize<McpOperationRequest>(
            """{"operationId":"group-by-alphabet-files","inputs":["input"]}""", Program.JsonOptions)!, sink, default, true);
        var inherited = executor.LastRequest!;
        await tools.RunOperation(JsonSerializer.Deserialize<McpOperationRequest>(
            """{"operationId":"group-by-alphabet-files","inputs":["input"],"recoveryVerification":"XxHash128","checkTiming":"BeforeBatch","options":{"RecurseFolders":true}}""", Program.JsonOptions)!, sink, default, true);
        var overridden = executor.LastRequest!;
        Assert.Equal(RecoveryVerificationMode.Sha256, inherited.RecoveryVerification);
        Assert.Equal(FileCheckTiming.BeforeItem, inherited.CheckTiming);
        Assert.Equal(FileCheckTiming.BeforeBatch, overridden.CheckTiming);
        Assert.False(inherited.SaveResumableRun);
        Assert.False(inherited.Options.RecurseFolders);
        Assert.Equal(RecoveryVerificationMode.XxHash128, overridden.RecoveryVerification);
        Assert.True(overridden.Options.RecurseFolders);
        Assert.False(overridden.Options.DeleteOldFiles);
        tools.SetSetting("recovery.check-timing", JsonSerializer.SerializeToElement("BeforeBatch"), true);
        await tools.RunOperation(JsonSerializer.Deserialize<McpOperationRequest>(
            """{"operationId":"zip-to-bin","inputs":["input"]}""", Program.JsonOptions)!, sink, default, true);
        Assert.Equal(FileCheckTiming.BeforeBatch, executor.LastRequest!.CheckTiming);
        Assert.Equal(RecoveryVerificationMode.Sha256, executor.LastRequest.RecoveryVerification);
    }

    private sealed class FakeExecutor : IOperationExecutor
    {
        internal int Calls;
        internal OperationRequest? LastRequest;
        public Task<ExecutionResult> ExecuteAsync(OperationRequest request, IProgress<ConversionProgress>? progress, CancellationToken token)
        {
            LastRequest = request;
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
    public async Task StdioTrackGroupingPreviewsAndRequiresBothWritePermissions()
    {
        string root = TestMediaFixture.NewFolder(), destination = TestMediaFixture.NewFolder();
        string source = Path.Combine(root, "BKD33601.zip");
        File.WriteAllText(source, "source");
        var arguments = new Dictionary<string, object?>
        {
            ["request"] = new
            {
                operationId = "group-by-track-id",
                inputs = new[] { source },
                outputFolder = destination,
                grouping = new { rangeSize = 50, rangeSeparator = " to " }
            }
        };
        await using var readOnly = await Connect();
        var preview = await readOnly.CallToolAsync("plan_operation", arguments);
        Assert.NotEqual(true, preview.IsError);
        using (var json = Json(preview))
            Assert.Contains("BKD-33601 to BKD-33650", json.RootElement.GetRawText());
        arguments["confirmFileChanges"] = true;
        Assert.True((await readOnly.CallToolAsync("run_operation", arguments)).IsError);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));

        var executor = new OperationExecutionService(new(TestMediaFixture.NewFolder()));
        using var writable = new McpOperationsService(new(true), executor, executor);
        var request = JsonSerializer.Deserialize<OperationRequest>(JsonSerializer.Serialize(arguments["request"]), Program.JsonOptions)!;
        Assert.Equal(KaraokeToolkit.Core.Recovery.RecoveryVerificationMode.XxHash128, request.RecoveryVerification);
        await Assert.ThrowsAsync<McpException>(() => writable.RunAsync(request, false, null, default));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        var result = await writable.RunAsync(request, true, null, default);
        Assert.False(result.HasIssues);
        Assert.Equal("source", File.ReadAllText(Path.Combine(destination, "BKD-33601 to BKD-33650", "BKD33601.zip")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task StdioClientDiscoversSchemasAndReadOnlyModeCannotWrite()
    {
        await using var client = await Connect();
        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "run_operation"); Assert.Contains(tools, t => t.Name == "get_settings");
        Assert.Contains(tools, t => t.Name == "list_settings");
        Assert.Contains(tools, t => t.Name == "get_setting");
        Assert.Contains(tools, t => t.Name == "set_setting");
        var schema = tools.Single(t => t.Name == "run_operation").JsonSchema.GetRawText();
        Assert.Contains("request", schema); Assert.Contains("operationId", schema); Assert.Contains("confirmFileChanges", schema);
        using var operations = Json(await client.CallToolAsync("get_operations"));
        Assert.False(operations.RootElement.GetProperty("allowFileChanges").GetBoolean());
        Assert.Equal(Enum.GetValues<OperationId>().Length, operations.RootElement.GetProperty("operations").GetArrayLength());
        var alphabetical = operations.RootElement.GetProperty("operations").EnumerateArray()
            .Single(entry => entry.GetProperty("coreId").GetString() == "OrganizeFiles");
        Assert.Equal("group-by-alphabet-files", alphabetical.GetProperty("name").GetString());
        using var settings = Json(await client.CallToolAsync("get_settings"));
        Assert.Equal(320, settings.RootElement.GetProperty("defaults").GetProperty("options").GetProperty("audioBitrate").GetInt32());
        Assert.Equal("XxHash128", settings.RootElement.GetProperty("defaultFileMoveRecoveryVerification").GetString());
        string root = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(root, OperationId.ZipToBin, "keep");
        var alphabeticalPlan = await client.CallToolAsync("plan_operation", new Dictionary<string, object?>
        {
            ["request"] = new { operationId = "group-by-alphabet-files", inputs = new[] { root } }
        });
        Assert.False(alphabeticalPlan.IsError ?? false);
        using var grouped = Json(alphabeticalPlan);
        Assert.Equal("OrganizeFiles", grouped.RootElement.GetProperty("report").GetProperty("operation").GetString());
        var result = await client.CallToolAsync("run_operation", new Dictionary<string, object?>
        { ["request"] = new { operationId = "zip-to-audiog", inputs = new[] { root } }, ["confirmFileChanges"] = true });
        Assert.True(result.IsError); Assert.False(File.Exists(Path.Combine(root, "song.wav")));
        var check = await client.CallToolAsync("validate_karaoke_files", new Dictionary<string, object?> { ["inputs"] = new[] { root } });
        Assert.False(check.IsError ?? false); using var report = Json(check);
        Assert.Equal("Validation Report", report.RootElement.GetProperty("report").GetProperty("reportType").GetString());
    }

    [Fact]
    public async Task StdioProcessingKeepsOriginalsAndReturnsStructuredReport()
    {
        await using var client = await Connect(true);
        string root = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(root, OperationId.ZipToBin, "keep");
        var request = new { operationId = "zip-to-audiog", inputs = new[] { root }, options = new { AutoCreateCueText = false, DeleteOldFiles = false } };
        var plan = await client.CallToolAsync("plan_operation", new Dictionary<string, object?> { ["request"] = request });
        Assert.False(plan.IsError ?? false); Assert.False(File.Exists(Path.Combine(root, "song.wav")));
        var denied = await client.CallToolAsync("run_operation", new Dictionary<string, object?> { ["request"] = request });
        Assert.True(denied.IsError);
        var result = await client.CallToolAsync("run_operation", new Dictionary<string, object?> { ["request"] = request, ["confirmFileChanges"] = true });
        Assert.False(result.IsError ?? false); Assert.True(File.Exists(Path.Combine(root, "song.wav"))); Assert.True(File.Exists(Path.Combine(root, "song.zip")));
        Assert.NotNull(result.StructuredContent); using var json = Json(result);
        Assert.Equal(1, json.RootElement.GetProperty("found").GetInt32());
    }

    /// <summary>Reads protocol stdout directly to verify ordered progress and final completion before the tool response.</summary>
    /// <remarks>SDK client callbacks can run concurrently, so callback arrival order cannot prove wire order.</remarks>
    [Fact]
    public async Task StdioProgressIsMonotonicOnTheWireBeforeTheToolResponse()
    {
        string root = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(root, OperationId.ZipToBin, "keep");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(McpTools).Assembly.Location);
        start.ArgumentList.Add("--allow-writes");
        using var process = System.Diagnostics.Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var diagnostics = process.StandardError.ReadToEndAsync();
        var values = new List<float>();

        async Task Send(object message)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
        }

        async Task<JsonElement> ReadResponse(int id)
        {
            while (true)
            {
                string line = await process.StandardOutput.ReadLineAsync(timeout.Token)
                    ?? throw new IOException("MCP exited before returning its response.");
                using var message = JsonDocument.Parse(line);
                var value = message.RootElement;
                if (value.TryGetProperty("method", out var method) && method.GetString() == "notifications/progress")
                {
                    var parameters = value.GetProperty("params");
                    Assert.Equal("wire-test", parameters.GetProperty("progressToken").GetString());
                    values.Add(parameters.GetProperty("progress").GetSingle());
                }
                if (value.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
                    return value.Clone();
            }
        }

        try
        {
            await Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new
            {
                protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "wire-test", version = "1.0" }
            } });
            Assert.True((await ReadResponse(1)).TryGetProperty("result", out _));
            await Send(new { jsonrpc = "2.0", method = "notifications/initialized" });
            await Send(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new
            {
                name = "run_operation",
                arguments = new { request = new { operationId = "zip-to-audiog", inputs = new[] { root },
                    options = new { AutoCreateCueText = false, DeleteOldFiles = false } }, confirmFileChanges = true },
                _meta = new { progressToken = "wire-test" }
            } });
            var response = await ReadResponse(2);
            Assert.True(response.TryGetProperty("result", out var result));
            Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean());
            // Read stdout directly: the SDK's concurrent client callbacks are not wire order.
            Assert.NotEmpty(values);
            Assert.Equal(values.Order(), values);
            Assert.Equal(100, values[^1]);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await diagnostics.WaitAsync(TimeSpan.FromSeconds(5));
        }
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
        Assert.False(result.IsError ?? false, string.Join(Environment.NewLine,
            result.Content.OfType<TextContentBlock>().Select(block => block.Text)));
        using var report = Json(result);
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

    [Theory]
    [InlineData("FutureUnknownOperation")]
    [InlineData("OrganizeFiles")]
    public async Task UnknownOperationIsAnActionableMcpToolError(string name)
    {
        await using var client = await Connect();
        var result = await client.CallToolAsync("run_operation", new Dictionary<string, object?>
        { ["request"] = new { operationId = name, inputs = new[] { "unused" } } });
        Assert.True(result.IsError);
        string message = result.Content.OfType<TextContentBlock>().First().Text;
        Assert.Contains("Unregistered MCP operation", message);
        Assert.Contains(name, message);
        Assert.Contains("get_operations", message);
    }

    [Fact]
    public async Task ExcludedOperationReturnsItsReasonToTheAiClient()
    {
        await using var client = await Connect();
        var result = await client.CallToolAsync("run_operation", new Dictionary<string, object?>
        { ["request"] = new { operationId = "play-audiog", inputs = new[] { "unused" } } });
        Assert.True(result.IsError);
        string message = result.Content.OfType<TextContentBlock>().First().Text;
        Assert.Contains("play-audiog", message); Assert.Contains("excluded", message);
        Assert.Contains("Playback is deferred", message); Assert.Contains("get_operations", message);
    }
}
