using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.Tests.App.Main;

public class HomeNavigationTests
{
    private static MainViewModel Model() => new(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));

    [Fact]
    public void CategorySelectionKeepsOperationAndInputScopeInSync()
    {
        var model = Model();
        Assert.Equal(OperationSection.KaraokeConversion, model.SelectedSection!.Section);
        foreach (var section in model.VisibleSections)
        {
            model.SelectedSection = section;
            Assert.Contains(model.SelectedOperation, model.VisibleOperations);
            Assert.Equal(section.Header, model.SelectedSectionTitle);
        }
        model.SelectedOperation = model.Operations.Single(operation => operation.IsRestore);
        Assert.Equal(OperationSection.ValidationAndReports, model.SelectedSection!.Section);
        Assert.False(model.NeedsSource);
        Assert.Contains("manifest", model.SourceInstruction);
        model.SelectedOperation = model.Operations.First(operation => operation.IsFlatten);
        Assert.True(model.NeedsSource);
        Assert.False(model.CanChooseFiles);
    }

    [Fact]
    public void RemovingLastFavouriteReturnsToItsNormalCategory()
    {
        var model = Model();
        var operation = model.Operations.Single(operation => operation.Action == OperationId.RepackZip);
        operation.ToggleFavouriteCommand.Execute(null);
        model.SelectedSection = model.VisibleSections.Single(section => section.Section == OperationSection.Favourites);
        Assert.Same(operation, model.SelectedOperation);
        operation.ToggleFavouriteCommand.Execute(null);
        Assert.Same(operation, model.SelectedOperation);
        Assert.Equal(OperationSection.Archives, model.SelectedSection!.Section);
        Assert.DoesNotContain(model.VisibleSections, section => section.Section == OperationSection.Favourites);
    }

    [Fact]
    public async Task SavedRippingFavouriteStillLaunchesAndFollowsExperimentalSetting()
    {
        string settings = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var model = new MainViewModel(settings);
        model.SaveOptions(model.Options with { ExperimentalDiscRipping = true });
        var ripping = model.Operations.Single(operation => operation.IsRip);
        ripping.ToggleFavouriteCommand.Execute(null);
        model = new MainViewModel(settings);
        model.SelectedSection = model.VisibleSections.Single(section => section.Section == OperationSection.Favourites);
        Assert.True(model.SelectedOperation!.IsRip);
        Assert.False(model.NeedsSource);
        Assert.Contains("drive", model.SourceInstruction);
        bool opened = false;
        model.RipDiscAsync = () =>
        {
            opened = true;
            return Task.FromResult<ConversionResult?>(null);
        };
        await model.StartCommand.ExecuteAsync(null);
        Assert.True(opened);
        model.SaveOptions(model.Options with { ExperimentalDiscRipping = false });
        Assert.False(model.SelectedOperation!.IsRip);
        Assert.Contains(model.SelectedSection, model.VisibleSections);
        Assert.DoesNotContain(model.FavouriteOperations, operation => operation.IsRip);
        model.SaveOptions(model.Options with { ExperimentalDiscRipping = true });
        Assert.Single(model.FavouriteOperations, operation => operation.IsRip);
    }

    [Fact]
    public void AddingInputsPreservesChosenOperationAndClearResetsReadiness()
    {
        var model = Model();
        var operation = model.Operations.Single(operation => operation.Action == OperationId.KaraokeToVideo);
        model.SelectedOperation = operation;
        string folder = TestMediaFixture.NewFolder();
        string first = Path.Combine(folder, "first.zip"), second = Path.Combine(folder, "second.zip");
        File.WriteAllText(first, "selection only");
        File.WriteAllText(second, "selection only");
        model.AddInputs([first]);
        model.AddInputs([second, first]);
        Assert.Same(operation, model.SelectedOperation);
        Assert.Equal(2, model.SelectedInputs.Count);
        Assert.Contains("2 selected inputs", model.InputSummary);
        Assert.True(model.CanStart);
        model.IsBusy = true;
        Assert.False(model.ClearInputsCommand.CanExecute(null));
        model.AddInputs([folder]);
        Assert.Equal(2, model.SelectedInputs.Count);
        model.IsBusy = false;
        model.ClearInputsCommand.Execute(null);
        Assert.Empty(model.SelectedInputs);
        Assert.Equal("", model.FolderPath);
        Assert.False(model.CanStart);
        Assert.False(model.CanClearInputs);
    }

    [Fact]
    public void ExternalFileRoutingStillSelectsTheCorrespondingCategory()
    {
        var model = Model();
        model.SelectedSection = model.VisibleSections.Single(section => section.Section == OperationSection.Archives);
        // Preserve the existing external input routing behaviour independently of Add buttons.
        model.SelectedOperation = model.Operations.Single(operation => operation.Action == OperationId.ArchiveToZip);
        string file = Path.Combine(TestMediaFixture.NewFolder(), "song.zip");
        File.WriteAllText(file, "selection only");
        model.SetInputs([file]);
        Assert.Equal(OperationId.ZipToAudioG, model.SelectedOperation!.Action);
        Assert.Equal(OperationSection.KaraokeConversion, model.SelectedSection!.Section);
    }
}
