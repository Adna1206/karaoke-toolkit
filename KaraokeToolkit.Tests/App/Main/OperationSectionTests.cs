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
        Assert.Equal(model.Operations.Count(operation => operation.Available && !operation.IsRip), grouped.Length);
        Assert.DoesNotContain(grouped, operation => operation.IsRip);
        Assert.Equal(grouped.Length, grouped.Distinct().Count());
        Assert.All(model.OperationSections, section => Assert.All(section.Operations, operation => Assert.Equal(section.Section, operation.Section)));
        Assert.DoesNotContain(grouped, operation => !operation.Available);
        Assert.Equal(2, model.OperationSections.Single(section => section.Section == OperationSection.Archives).Operations.Count);
        var fileTools = model.OperationSections.Single(section => section.Section == OperationSection.FileTools);
        var folderTools = model.OperationSections.Single(section => section.Section == OperationSection.FolderTools);
        Assert.Equal("File Tools", fileTools.Header);
        Assert.Equal("Folder Tools", folderTools.Header);
        Assert.Equal(new[]
        {
            OperationId.CleanUpFilenames, OperationId.AddTrackIdSeparator, OperationId.RemoveTrackIdSeparator,
            OperationId.TagKaraoke, OperationId.NormalizeAudioGain, OperationId.ChangeAudioKey, OperationId.RenameByPattern
        }, fileTools.Operations.Select(operation => operation.Definition.Operation!.Value));
        Assert.Equal(new[]
        {
            OperationId.GroupByTrackId, OperationId.OrganizeFiles, OperationId.FlattenOneLayer, OperationId.FlattenAllLayers
        }, folderTools.Operations.Select(operation => operation.Definition.Operation!.Value));
        Assert.Equal("Group by Alphabet", Assert.Single(folderTools.Operations, operation => operation.IsOrganizer).Label);
        Assert.Contains(OperationDefinitions.All, definition => definition.Operation == OperationId.OrganizeFolders && definition.CanUseInRecipes);
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
        var files = model.OperationSections.Single(section => section.Section == OperationSection.FileTools);
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

    [Fact]
    public void SplitSectionsInheritTheLegacyPreferenceUntilSavedIndependently()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        File.WriteAllText(path, """
            <KaraokeToolkit schemaVersion="1">
              <Desktop><OperationSections><FilesAndFolders>false</FilesAndFolders></OperationSections></Desktop>
            </KaraokeToolkit>
            """);
        var model = new MainViewModel(path);
        Assert.False(model.OperationSections.Single(section => section.Section == OperationSection.FileTools).IsExpanded);
        var folders = model.OperationSections.Single(section => section.Section == OperationSection.FolderTools);
        Assert.False(folders.IsExpanded);
        folders.IsExpanded = true;

        var reopened = new MainViewModel(path);
        Assert.False(reopened.OperationSections.Single(section => section.Section == OperationSection.FileTools).IsExpanded);
        Assert.True(reopened.OperationSections.Single(section => section.Section == OperationSection.FolderTools).IsExpanded);
    }
}
