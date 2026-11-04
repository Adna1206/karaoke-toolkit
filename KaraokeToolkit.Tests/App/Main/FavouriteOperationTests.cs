using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Tests.App.Main;

public class FavouriteOperationTests
{
    private static string SettingsPath() => Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
    private static OperationSectionViewModel Favourites(MainViewModel model) => model.OperationSections[0];

    [Fact]
    public void StarsAddAndRemoveSharedOperationsWithoutChangingSelection()
    {
        var model = new MainViewModel(SettingsPath());
        var selected = model.SelectedOperation;
        var archive = model.OperationSections.Single(section => section.Section == OperationSection.Archives).Operations[0];
        Assert.Equal(OperationSection.Favourites, Favourites(model).Section);
        Assert.False(Favourites(model).IsVisible);
        Assert.Equal("☆", archive.FavouriteIcon);
        archive.ToggleFavouriteCommand.Execute(null);
        Assert.True(Favourites(model).IsVisible);
        Assert.Same(archive, Assert.Single(Favourites(model).Operations));
        Assert.Same(selected, model.SelectedOperation);
        Assert.Equal("★", archive.FavouriteIcon);
        Assert.Contains("Remove", archive.FavouriteHint);
        Assert.Contains(archive.Label, archive.FavouriteAccessibleName);
        archive.ToggleFavouriteCommand.Execute(null);
        Assert.Empty(Favourites(model).Operations);
        Assert.False(Favourites(model).IsVisible);
        Assert.Same(selected, model.SelectedOperation);
        Assert.Contains(archive, model.OperationSections.Single(section => section.Section == OperationSection.Archives).Operations);
    }

    [Fact]
    public void FavouritesPersistInNormalOrderAndKeepExistingPreferences()
    {
        string path = SettingsPath();
        var model = new MainViewModel(path);
        var first = model.Operations[0];
        var last = model.Operations.Last();
        last.ToggleFavouriteCommand.Execute(null);
        first.ToggleFavouriteCommand.Execute(null);
        model.SaveOptions(model.Options with { AutoCreateCueText = false });
        Favourites(model).IsExpanded = false;
        var reopened = new MainViewModel(path);
        Assert.Equal(new[] { first.Id, last.Id }, reopened.FavouriteOperations.Select(operation => operation.Id));
        Assert.True(Favourites(reopened).IsVisible);
        Assert.False(Favourites(reopened).IsExpanded);
        Assert.False(reopened.Options.AutoCreateCueText);
        var favourite = Favourites(reopened).Operations[0];
        reopened.SelectedOperation = favourite;
        Assert.Same(reopened.Operations[0], reopened.SelectedOperation);
        Assert.True(Favourites(reopened).IsExpanded);
        Assert.True(reopened.OperationSections.Single(section => section.Section == favourite.Section).IsExpanded);
        foreach (var operation in reopened.FavouriteOperations) operation.ToggleFavouriteCommand.Execute(null);
        Assert.Empty(new MainViewModel(path).FavouriteOperations);
    }

    [Fact]
    public void DisabledRippingFavouriteIsHiddenButRemembered()
    {
        string path = SettingsPath();
        var model = new MainViewModel(path);
        model.SaveOptions(model.Options with { ExperimentalDiscRipping = true });
        var rip = model.Operations.Single(operation => operation.IsRip);
        rip.ToggleFavouriteCommand.Execute(null);
        Assert.Same(rip, Assert.Single(model.FavouriteOperations));
        model.SaveOptions(model.Options with { ExperimentalDiscRipping = false });
        Assert.False(Favourites(model).IsVisible);
        var reopened = new MainViewModel(path);
        Assert.True(reopened.Operations.Single(operation => operation.IsRip).IsFavourite);
        Assert.Empty(reopened.FavouriteOperations);
        reopened.SaveOptions(reopened.Options with { ExperimentalDiscRipping = true });
        Assert.Single(reopened.FavouriteOperations);
    }

    [Fact]
    public void StableIdsIgnoreDisplayLabelsAndUnknownSavedIds()
    {
        string path = SettingsPath();
        new ApplicationSettingsStore(path).Save(new(), favouriteOperations: ["ZipToBin", "ZipToBin", "unknown-operation", "RenameFilesFromInternet"]);
        var model = new MainViewModel(path);
        var operation = Assert.Single(model.FavouriteOperations);
        Assert.Equal(operation.Id, new ActionChoice(operation.Definition with { Label = "Changed display name" }).Id);
        Assert.Equal(model.Operations.Count, model.Operations.Select(choice => choice.Id).Distinct().Count());
    }
}
