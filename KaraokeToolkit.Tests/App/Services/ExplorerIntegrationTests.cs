using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Main;
using static KaraokeToolkit.Tests.Support.FileWorkflowFixture;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Validation;
using System.IO.Compression;
using System.Text.Json;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.App.Services;

public class ExplorerIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetupEnablesNewProfilesAndPreservesDisabledRegistrationOnRepeat(bool existingSettings)
    {
        if (!OperatingSystem.IsWindows()) return;
        string hive = @"Software\KaraokeToolkitTests\" + Guid.NewGuid().ToString("N");
        string folder = TestMediaFixture.NewFolder();
        string executable = Path.Combine(folder, "test.exe");
        string settings = Path.Combine(folder, "options.xmls");
        File.WriteAllText(executable, "not executed");
        if (existingSettings) File.WriteAllText(settings, "<profile />");

        try
        {
            var integration = new ExplorerIntegration(executable, hive);
            integration.ApplySetupDefault(settings);
            Assert.Equal(!existingSettings, integration.IsInstalled());

            integration.Remove();
            File.Delete(settings);
            integration.ApplySetupDefault(settings);
            Assert.False(integration.IsInstalled());
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(hive, false); }
    }

    [Fact]
    public void ExplorerCommandsQuotePathsAndDoNotChangeDefaultAssociations()
    {
        var entries = ExplorerIntegration.Entries(@"C:\Program Files\KaraokeToolkit\KaraokeToolkit.App.exe");
        Assert.All(entries, e => { Assert.Contains(@"\shell\KaraokeToolkit", e.Key); Assert.StartsWith("\"C:\\Program Files\\KaraokeToolkit\\KaraokeToolkit.App.exe\" --open \"", e.Command); });
        Assert.Contains(entries, e => e.Key.StartsWith(@"Directory\Background") && e.Command.EndsWith("\"%V\""));
        Assert.Equal(InputSelection.Extensions.Length + 2, entries.Count);
        Assert.Throws<ArgumentException>(() => ExplorerIntegration.Entries("relative.exe"));
    }
    [Fact]
    public void ExplorerAndDragInputUseTheSameSelectionValidation()
    {
        string folder = ZipFolder(), path = Path.Combine(folder, "song.zip");
        Assert.Equal(new[] { path }, ExplorerIntegration.ParseInputs(["--open", path]));
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "options.xmls"));
        model.SetInputs([path]);
        Assert.Equal(Action.ZipToAudioG, model.SelectedOperation!.Action);
        Assert.Equal(new[] { path }, model.SelectedInputs);
        model.FolderPath = folder;
        Assert.Empty(model.SelectedInputs);
    }
    [Fact]
    public void ExplorerInstallAndRemoveUseOnlyOwnedKeys()
    {
        if (!OperatingSystem.IsWindows())
            return;
        string hive = @"Software\KaraokeToolkitTests\" + Guid.NewGuid().ToString("N");
        string exe = Path.Combine(TestMediaFixture.NewFolder(), "test app.exe");
        File.WriteAllText(exe, "not executed");
        try
        {
            var integration = new ExplorerIntegration(exe, hive);
            Assert.False(integration.IsInstalled());
            integration.Install();
            Assert.True(integration.IsInstalled());
            using (var other = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(hive + @"\Directory\shell\OtherApp"))
                other.SetValue("", "leave alone");
            integration.Install();
            integration.Remove();
            Assert.False(integration.IsInstalled());
            using var retained = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(hive + @"\Directory\shell\OtherApp");
            Assert.Equal("leave alone", retained!.GetValue(""));
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(hive, false); }
    }
    [Fact]
    public void ExplorerRefusesToReplaceAnUnownedEntry()
    {
        if (!OperatingSystem.IsWindows())
            return;
        string hive = @"Software\KaraokeToolkitTests\" + Guid.NewGuid().ToString("N");
        string exe = Path.Combine(TestMediaFixture.NewFolder(), "test.exe");
        File.WriteAllText(exe, "not executed");
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(hive + @"\Directory\shell\KaraokeToolkit"))
                key.SetValue("", "someone else's command");
            var integration = new ExplorerIntegration(exe, hive);
            Assert.Throws<IOException>(integration.Install);
            integration.Remove();
            using var retained = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(hive + @"\Directory\shell\KaraokeToolkit");
            Assert.Equal("someone else's command", retained!.GetValue(""));
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(hive, false); }
    }
}
