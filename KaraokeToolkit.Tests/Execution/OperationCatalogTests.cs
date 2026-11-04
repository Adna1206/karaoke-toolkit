using System.Text.Json;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Cli;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Tests.Execution;

public class OperationCatalogTests
{
    [Fact]
    public void EveryOperationRoundTripsByName()
    {
        foreach (var operation in Enum.GetValues<OperationId>())
        {
            string json = JsonSerializer.Serialize(operation);
            Assert.Equal("\"" + operation + "\"", json);
            Assert.Equal(operation, JsonSerializer.Deserialize<OperationId>(json));
        }
        Assert.Equal(Enum.GetNames<OperationId>().Length, Enum.GetValues<OperationId>().Distinct().Count());
    }

    [Fact]
    public void RecoveryPersistsNamedPoliciesStatusesAndNestedRetryOperations()
    {
        string root = TestMediaFixture.NewFolder();
        string path = Path.Combine(root, "checkpoint.json");
        var retry = new ConversionRetry(OperationId.ZipToAudioG, [root], new(), OverwritePolicy.Replace);
        var result = new ConversionResult([new(root, ConversionStatus.Succeeded, [], "Done")], false)
        {
            RetryRequests = [retry]
        };
        DurableJson.Write(path, new WorkflowData
        {
            Kind = RecoveryKind.Recipe,
            Conversions = [new() { Folder = root, Sources = [], Result = result }]
        });
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("Recipe", json.RootElement.GetProperty("Kind").GetString());
        var saved = json.RootElement.GetProperty("Conversions")[0].GetProperty("Result");
        Assert.Equal("Succeeded", saved.GetProperty("Files")[0].GetProperty("Status").GetString());
        Assert.Equal("ZipToAudioG", saved.GetProperty("RetryRequests")[0].GetProperty("Action").GetString());
        Assert.Equal("Replace", saved.GetProperty("RetryRequests")[0].GetProperty("Policy").GetString());
        var restored = JsonSerializer.Deserialize<WorkflowData>(File.ReadAllText(path), DurableJson.Settings)!;
        Assert.Equal(OperationId.ZipToAudioG, restored.Conversions![0].Result!.RetryRequests[0].Action);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("\"0\"")]
    [InlineData("\"Unknown\"")]
    [InlineData("\"ziptoaudiog\"")]
    public void NumericOrUnknownOperationIdentitiesAreRejected(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OperationId>(json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConversionRequest>(
            "{\"Action\":" + json + ",\"Folder\":\".\",\"Options\":{}}"));
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(2, "1")]
    [InlineData(1, "\"ZipToAudioG\"")]
    public void UnsupportedRecoveryJournalsCannotBeResumed(int version, string operation)
    {
        string root = TestMediaFixture.NewFolder();
        string directory = Directory.CreateDirectory(Path.Combine(root, ".karaoke-job-run-" + Guid.NewGuid().ToString("N"))).FullName;
        string journal = Path.Combine(directory, "run.json");
        string json = JsonSerializer.Serialize(new RunData { Request = new(OperationId.ZipToAudioG, root, new()) }, DurableJson.Settings);
        json = json.Replace("\"Version\":2", "\"Version\":" + version).Replace("\"Action\":\"ZipToAudioG\"", "\"Action\":" + operation);
        File.WriteAllText(journal, json);
        var error = Record.Exception(() => RunSession.Read(journal));
        Assert.True(error is IOException or JsonException);
        Assert.NotNull(Assert.Single(new RecoveryStore(TestMediaFixture.NewFolder()).Find(root)).Problem);
        Assert.Equal(json, File.ReadAllText(journal));
    }

    [Fact]
    public void DiscCaptureStatusDoesNotDowngradeOrdinaryFileConverters()
    {
        Assert.Equal(OperationSupport.HardwareUnverified, OperationCapabilities.Describe(OperationId.DiscRip));
        foreach (var conversion in new[] { OperationId.AudioGToBin, OperationId.AudioGToZip, OperationId.BinToAudioG, OperationId.BinToZip, OperationId.ZipToBin, OperationId.ZipToAudioG })
        {
            Assert.Equal(OperationSupport.Supported, OperationCapabilities.Describe(conversion));
            Assert.True(OperationCapabilities.IsConversion(conversion));
            Assert.True(OperationCapabilities.CanRunUnattended(conversion));
        }
    }

    [Fact]
    public void CoreAppRecipesAndCliShareIdentities()
    {
        Assert.Equal(Enum.GetValues<OperationId>().Length, OperationCatalog.All.Count);
        Assert.Equal(OperationCatalog.All.Count, OperationCatalog.All.Select(o => o.Identifier).Distinct().Count());
        foreach (var definition in OperationDefinitions.All)
        {
            Assert.NotNull(definition.Operation);
            Assert.Equal(definition.Id, OperationCatalog.Get(definition.Operation!.Value).Identifier);
            if (definition.Action is { } action)
            {
                Assert.Equal(action, definition.Operation.Value);
                Assert.True(OperationCapabilities.IsConversion(action) || OperationCapabilities.Describe(action) != OperationSupport.Supported);
            }
        }
        foreach (var operation in RecipeOperations.All) Assert.Equal(operation.Id, OperationCatalog.Get(operation.Operation).Identifier);
        var dynamicRecipe = new OperationDefinition("Recipe:" + Guid.NewGuid(), null, "Test", "Test", "Test", OperationSection.Recipes, OperationInputKind.Folders, true);
        Assert.Null(dynamicRecipe.Operation);
    }

    [Fact]
    public void CliExplicitlyIncludesOrExcludesEveryCoreOperation()
    {
        Assert.Equal(Enum.GetValues<OperationId>().Order(), CliOperationCatalog.All.Select(o => o.OperationId).Order());
        Assert.Equal(CliOperationCatalog.All.Count, CliOperationCatalog.All.Select(o => o.OperationId).Distinct().Count());
        Assert.Equal(CliOperationCatalog.Exposed.Count, CliOperationCatalog.Exposed.Select(o => o.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var registration in CliOperationCatalog.All)
        {
            if (registration.Exposed)
            {
                Assert.Null(registration.ExclusionReason); Assert.False(string.IsNullOrWhiteSpace(registration.Name));
                Assert.True(OperationCapabilities.CanRunUnattended(registration.OperationId));
                Assert.Equal(registration, CliOperationCatalog.Resolve(registration.Name!));
            }
            else
            {
                Assert.Null(registration.Name); Assert.False(string.IsNullOrWhiteSpace(registration.ExclusionReason));
                Assert.Throws<CliUsageException>(() => CliOperationCatalog.Resolve(registration.OperationId.ToString()));
            }
        }
        Assert.Equal("restore-quarantined-files", CliOperationCatalog.Get(OperationId.RestoreQuarantined).Name);
    }

    [Fact]
    public async Task NamedRecoveryJournalResumes()
    {
        string folder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(folder, OperationId.ZipToBin, "keep");
        string directory = Directory.CreateDirectory(Path.Combine(folder, ".karaoke-job-run-" + Guid.NewGuid().ToString("N"))).FullName;
        string journal = Path.Combine(directory, "run.json");
        // The preceding schema used a differently named CLR request/enum. Those
        // type names are not part of JSON: Action stays numeric and fields stay fixed.
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            Version = 2, Request = new { Action = "ZipToAudioG", Folder = folder, Options = new { DeleteOldFiles = false, AutoCreateCueText = false } },
            Policy = "Fail", Sources = new[] { Path.Combine(folder, "song.zip") }, Results = new { }, Metadata = Array.Empty<string>(), MetadataResults = new { }, Finished = false
        }));
        var data = RunSession.Read(journal); Assert.Equal(OperationId.ZipToAudioG, data.Request.Action);
        using var requestJson = JsonDocument.Parse(JsonSerializer.Serialize(data.Request));
        Assert.Equal("ZipToAudioG", requestJson.RootElement.GetProperty("Action").GetString());
        Assert.Equal(folder, requestJson.RootElement.GetProperty("Folder").GetString());
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        var result = await new ConversionService(store).ResumeAsync(Assert.Single(store.Find(folder)));
        Assert.Equal(1, result.Succeeded); Assert.Equal(0, result.Failed); Assert.True(File.Exists(Path.Combine(folder, "song.wav")));
        Assert.True(File.Exists(Path.Combine(folder, "song.zip"))); Assert.False(File.Exists(journal));
    }

    [Fact]
    public void ExistingRecipeStringsRemainStringsAndNonConversionsAreRejectedByConversionEngine()
    {
        string json = """{"FormatVersion":1,"Id":"37064eec-506d-44e9-a954-3e281d5bd8bc","Name":"Saved tags","Steps":[{"OperationId":"TagKaraoke"}]}""";
        var recipe = RecipeJson.Deserialize(json); Assert.Equal("TagKaraoke", Assert.Single(recipe.Steps).OperationId);
        using var saved = JsonDocument.Parse(RecipeJson.Serialize(recipe));
        Assert.Equal("TagKaraoke", saved.RootElement.GetProperty("Steps")[0].GetProperty("OperationId").GetString());
        Assert.Throws<NotSupportedException>(() => new ConversionRequest(OperationId.TagKaraoke, ".", new()).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => OperationCatalog.Get((OperationId)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => CliOperationCatalog.Get((OperationId)999));
    }
}
