using System.Text.Json;
using KaraokeToolkit.App.Features.Settings.Integrations;

namespace KaraokeToolkit.Tests.App.Settings.Integrations;

public class McpIntegrationTests
{
    [Theory]
    [InlineData(true, "karaoke-toolkit-mcp.exe")]
    [InlineData(false, "karaoke-toolkit-mcp")]
    public void ConfigurationUsesTheLocalExecutableAndStartsReadOnly(bool windows, string filename)
    {
        string folder = Path.Combine(TestMediaFixture.NewFolder(), "User's Toolkit");
        var model = new McpIntegrationViewModel(folder, windows, _ => true);
        Assert.True(model.IsAvailable); Assert.False(model.IsMissing); Assert.False(model.AllowFileChanges);
        using var config = JsonDocument.Parse(model.ConfigurationJson);
        var entry = config.RootElement.GetProperty("mcpServers").GetProperty("karaoke-toolkit");
        Assert.Equal(Path.Combine(folder, filename), entry.GetProperty("command").GetString());
        Assert.Empty(entry.GetProperty("args").EnumerateArray());
    }

    [Fact]
    public void WriteChoiceOnlyAddsStartupArgumentToTheGeneratedExample()
    {
        var model = new McpIntegrationViewModel(TestMediaFixture.NewFolder(), true, _ => true);
        int notifications = 0;
        model.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(model.ConfigurationJson)) notifications++; };
        model.FileAccess = model.FileAccessModes[1];
        using var enabled = JsonDocument.Parse(model.ConfigurationJson);
        Assert.Equal("--allow-writes", Assert.Single(enabled.RootElement.GetProperty("mcpServers").GetProperty("karaoke-toolkit").GetProperty("args").EnumerateArray()).GetString());
        model.FileAccess = model.FileAccessModes[0];
        using var disabled = JsonDocument.Parse(model.ConfigurationJson);
        Assert.Empty(disabled.RootElement.GetProperty("mcpServers").GetProperty("karaoke-toolkit").GetProperty("args").EnumerateArray());
        Assert.Equal(2, notifications);
        Assert.Empty(model.Status);
    }

    [Fact]
    public async Task CopyIsExplicitAndClipboardFailuresAreVisible()
    {
        var model = new McpIntegrationViewModel(TestMediaFixture.NewFolder(), true, _ => false);
        Assert.True(model.IsMissing); Assert.False(model.HasStatus);
        string? copied = null;
        await model.CopyAsync(model.ConfigurationJson, value => { copied = value; return Task.CompletedTask; });
        Assert.Equal(model.ConfigurationJson, copied); Assert.True(model.HasStatus);
        Assert.False(model.AllowFileChanges);
        await model.CopyAsync(model.ExecutablePath, _ => throw new IOException("clipboard busy"));
        Assert.Contains("clipboard busy", model.Status);
    }
}
