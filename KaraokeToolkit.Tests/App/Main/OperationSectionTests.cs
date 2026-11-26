using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.App.Features.Main;
using System.Xml.Linq;

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
        var namesAndTags = model.OperationSections.Single(section => section.Section == OperationSection.NamesAndTags);
        var audioTools = model.OperationSections.Single(section => section.Section == OperationSection.AudioTools);
        var organization = model.OperationSections.Single(section => section.Section == OperationSection.Organization);
        Assert.Equal("Names & Tags", namesAndTags.Header);
        Assert.Equal("Audio Tools", audioTools.Header);
        Assert.Equal("Organization", organization.Header);
        Assert.Equal(new[]
        {
            OperationId.CleanUpFilenames, OperationId.AddTrackIdSeparator, OperationId.RemoveTrackIdSeparator,
            OperationId.TagKaraoke, OperationId.RenameByPattern
        }, namesAndTags.Operations.Select(operation => operation.Definition.Operation!.Value));
        Assert.Equal(new[] { OperationId.NormalizeAudioGain, OperationId.ChangeAudioKey },
            audioTools.Operations.Select(operation => operation.Definition.Operation!.Value));
        Assert.Equal(new[]
        {
            OperationId.GroupByTrackId, OperationId.OrganizeFiles, OperationId.FlattenOneLayer, OperationId.FlattenAllLayers
        }, organization.Operations.Select(operation => operation.Definition.Operation!.Value));
        Assert.Equal("Group by Alphabet", Assert.Single(organization.Operations, operation => operation.IsOrganizer).Label);
        Assert.Contains(OperationDefinitions.All, definition => definition.Operation == OperationId.OrganizeFolders && definition.CanUseInRecipes);
        Assert.Equal(3, model.OperationSections.Single(section => section.Section == OperationSection.ValidationAndReports).Operations.Count);
    }

    [Fact]
    public void CategorySelectionLastsForTheCurrentSession()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var model = new MainViewModel(path);
        var audioTools = model.VisibleSections.Single(section => section.Section == OperationSection.AudioTools);
        model.SelectedSection = audioTools;
        model.SaveOptions(model.Options with { AutoCreateCueText = false });
        audioTools.Operations[0].ToggleFavouriteCommand.Execute(null);
        Assert.Same(audioTools, model.SelectedSection);
        Assert.Empty(XDocument.Load(path).Descendants("OperationSections"));
        Assert.Equal(OperationSection.KaraokeConversion, new MainViewModel(path).SelectedSection!.Section);
    }
}
