using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Tests.App.Main;

public class OperationSectionTests
{
    [Fact]
    public void AvailableOperationsAppearOnceInTheCorrectSection()
    {
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        var grouped = model.OperationSections.SelectMany(section => section.Operations).ToArray();
        Assert.Equal(model.Operations.Count(operation => operation.Available), grouped.Length);
        Assert.Equal(grouped.Length, grouped.Distinct().Count());
        Assert.All(model.OperationSections, section => Assert.All(section.Operations, operation => Assert.Equal(section.Section, operation.Section)));
        Assert.DoesNotContain(grouped, operation => !operation.Available);
        Assert.Equal(2, model.OperationSections.Single(section => section.Section == OperationSection.Archives).Operations.Count);
        var fileTools = model.OperationSections.Single(section => section.Section == OperationSection.FilesAndFolders).Operations;
        Assert.Equal(7, fileTools.Count);
        Assert.Contains(fileTools, operation => operation.Definition.Operation == OperationId.GroupByTrackId);
        Assert.Contains(model.OperationSections.Single(section => section.Section == OperationSection.FilesAndFolders).Operations, operation => operation.IsTagging);
        Assert.Equal(3, model.OperationSections.Single(section => section.Section == OperationSection.ValidationAndReports).Operations.Count);
    }

    [Fact]
    public void SectionPreferencesSurviveRestartAndOptionsSave()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var model = new MainViewModel(path);
        var archive = model.OperationSections.Single(section => section.Section == OperationSection.Archives);
        archive.IsExpanded = true;
        model.SaveOptions(model.Options with { AutoCreateCueText = false });
        var reopened = new MainViewModel(path);
        Assert.True(reopened.OperationSections.Single(section => section.Section == OperationSection.Archives).IsExpanded);
        archive.IsExpanded = false;
        Assert.False(new MainViewModel(path).OperationSections.Single(section => section.Section == OperationSection.Archives).IsExpanded);
        Assert.False(new SettingsStore(path).Load().AutoCreateCueText);
    }

    [Fact]
    public void SelectingOperationExpandsItsSectionAndKeepsItVisible()
    {
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        var files = model.OperationSections.Single(section => section.Section == OperationSection.FilesAndFolders);
        Assert.True(files.IsExpanded);
        files.IsExpanded = false;
        Assert.False(files.IsExpanded);
        model.SelectedOperation = files.Operations[0];
        Assert.True(files.IsExpanded);
        files.IsExpanded = false;
        Assert.True(files.IsExpanded);
        Assert.All(model.OperationSections, section => Assert.Same(model.SelectedOperation, section.SelectedOperation));
        model.SelectedOperation = model.Operations.First(operation => operation.Action == OperationId.ZipToAudioG);
        files.IsExpanded = false;
        Assert.False(files.IsExpanded);
    }
}
