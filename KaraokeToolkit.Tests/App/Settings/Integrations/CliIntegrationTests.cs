using KaraokeToolkit.App.Features.Settings.Integrations;

namespace KaraokeToolkit.Tests.App.Settings.Integrations;

public class CliIntegrationTests
{
    [Theory]
    [InlineData(CliShell.PowerShell, "''")]
    [InlineData(CliShell.Bash, "'\"'\"'")]
    [InlineData(CliShell.Zsh, "'\"'\"'")]
    public void CommandsQuoteInstallationPathsAndUpdateWithSelection(CliShell shell, string escapedQuote)
    {
        string folder = Path.Combine(TestMediaFixture.NewFolder(), "User's Toolkit");
        var model = new CliIntegrationViewModel(folder, true, shell, _ => true);
        Assert.EndsWith("karaoke-toolkit.exe", model.ExecutablePath);
        Assert.Contains("User" + escapedQuote + "s Toolkit", model.CommandText);
        Assert.EndsWith(" --help", model.CommandText);
        Assert.Contains("completion " + shell.ToString().ToLowerInvariant(), model.SetupText);
        Assert.Contains("path " + shell.ToString().ToLowerInvariant(), model.SetupText);
        Assert.False(model.ExampleNeedsFolder);
        model.ExampleIndex = 2;
        Assert.True(model.ExampleNeedsFolder);
        Assert.Contains(" validate-karaoke-files '<karaoke-folder>' --json --quiet", model.CommandText);
        model.ExampleIndex = 3;
        Assert.Contains(" preview-tags ", model.CommandText);
        model.ExampleIndex = 1;
        Assert.False(model.ExampleNeedsFolder);
        Assert.EndsWith(" operations", model.CommandText);
        model.Shell = CliShell.PowerShell;
        Assert.StartsWith("& '", model.CommandText);
        Assert.Contains("Out-String", model.SetupText);
    }

    [Fact]
    public async Task CopyAndTerminalActionsAreExplicitAndReportFailures()
    {
        string folder = TestMediaFixture.NewFolder();
        var model = new CliIntegrationViewModel(folder, false, CliShell.Bash, _ => true);
        Assert.Equal("", model.Status); Assert.False(model.HasStatus); Assert.False(model.IsMissing);
        string? copied = null, opened = null;
        await model.CopyAsync(model.ExecutablePath, value => { copied = value; return Task.CompletedTask; });
        Assert.Equal(model.ExecutablePath, copied); Assert.Equal("Copied to clipboard.", model.Status);
        Assert.True(model.HasStatus);
        model.OpenTerminal(value => opened = value);
        Assert.Equal(folder, opened); Assert.Contains("No CLI command was run", model.Status);
        await model.CopyAsync(model.CommandText, _ => throw new IOException("clipboard busy"));
        Assert.Contains("clipboard busy", model.Status);
        model.OpenTerminal(_ => throw new IOException("terminal unavailable"));
        Assert.Contains("terminal unavailable", model.Status);
    }

    [Fact]
    public void MissingPackagedCliExplainsAvailabilityAndCannotLaunch()
    {
        var model = new CliIntegrationViewModel(TestMediaFixture.NewFolder(), false, CliShell.Zsh);
        Assert.False(model.IsAvailable); Assert.True(model.IsMissing); Assert.EndsWith("karaoke-toolkit", model.ExecutablePath);
        Assert.Contains("Published app packages", model.Availability);
        model.OpenTerminal(_ => throw new Exception("Must not launch"));
        Assert.Equal(model.Availability, model.Status);
    }

    [Fact]
    public void WindowsTerminalLaunchUsesAnArgumentListAndNoCommandExecution()
    {
        if (!OperatingSystem.IsWindows()) return;
        string folder = TestMediaFixture.NewFolder();
        var start = CliTerminalLauncher.CreateStartInfo(folder);
        Assert.Equal(folder, start.WorkingDirectory);
        Assert.True(start.UseShellExecute);
        Assert.Equal(new[] { "-NoProfile", "-NoExit" }, start.ArgumentList);
        Assert.DoesNotContain("-Command", start.ArgumentList);
        Assert.True(File.Exists(start.FileName));
    }
}
