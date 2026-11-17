using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Tagging;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Tagging;
using KaraokeToolkit.Tests.Tagging;

namespace KaraokeToolkit.Tests.App.Tagging;

public class TaggingViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledOrFailedTaggingDialogLeavesTheScreenUsable(bool fail)
    {
        string folder = TestMediaFixture.NewFolder();
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        model.SelectedOperation = model.Operations.Single(operation => operation.IsTagging);
        model.SetInputs([folder]);
        model.ConfigureTaggingAsync = _ => fail
            ? Task.FromException<TaggingRequest?>(new IOException("Dialog failed"))
            : Task.FromResult<TaggingRequest?>(null);

        await model.StartCommand.ExecuteAsync(null);

        Assert.False(model.IsBusy);
        Assert.False(model.IsRecoveryMode);
        Assert.False(model.ShowProgress);
        Assert.Null(model.LastReport);
        Assert.True(model.CanStart);
        if (fail) Assert.Contains("Dialog failed", model.Status);

        model.SelectedOperation = model.Operations.Single(operation => operation.IsValidation);
        await model.StartCommand.ExecuteAsync(null);
        Assert.False(model.IsBusy);
        Assert.Equal("Validation Report", model.LastReport!.ReportType);
    }

    [Fact]
    public void PatternHistoryIsUniquePersistentAndSurvivesOtherSettingsSaves()
    {
        string filename = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        const string custom = "%DISC%. %ARTIST% - %TITLE%";
        var store = new ApplicationSettingsStore(filename);
        store.Save(new(), taggingPatterns: [FilenameTagPattern.Default, custom, custom], lastTaggingPattern: custom);
        store.Save(new() { AudioBitrate = 192 }, theme: ApplicationTheme.Dark);
        var reopened = new ApplicationSettingsStore(filename);
        Assert.Equal(new[] { FilenameTagPattern.Default, custom }, reopened.LoadTaggingPatterns());
        Assert.Equal(custom, reopened.LoadLastTaggingPattern());
        Assert.Equal(192, reopened.Load().AudioBitrate);
        Assert.Equal(ApplicationTheme.Dark, reopened.LoadTheme());
    }

    [Fact]
    public async Task EditingPatternRequiresFreshPreviewAndFieldCorrectionsAreValidated()
    {
        string path = await KaraokeTaggingTests.CreateMedia(".wav");
        var model = new TaggingViewModel([path], false, [FilenameTagPattern.Default], FilenameTagPattern.Default);
        await model.PreviewAsync();
        Assert.True(model.CanApply);
        var row = Assert.Single(model.Rows);
        Assert.False(model.HasEdits);
        row.Artist = "Corrected artist"; row.Track = "bad";
        Assert.True(model.HasEdits);
        Assert.Null(model.BuildRequest()); Assert.Contains("0 or greater", model.Status);
        row.Track = "7";
        var request = model.BuildRequest()!;
        Assert.Equal("Corrected artist", request.Files[0].Tags!.Artist); Assert.Equal(7u, request.Files[0].Tags!.Track);
        Assert.Equal(row.Preview.SourceHash, request.Files[0].SourceHash);
        row.Track = "0000";
        Assert.Equal(0u, model.BuildRequest()!.Files[0].Tags!.Track);
        model.Pattern = "%TITLE%";
        Assert.False(model.CanApply); Assert.Null(model.BuildRequest());
        await model.PreviewAsync();
        Assert.True(model.CanApply);
        Assert.Null(model.BuildRequest()!.Files[0].Tags!.Album);
        row = Assert.Single(model.Rows);
        int applyChanges = 0;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TaggingViewModel.CanApply)) applyChanges++;
        };
        row.Included = false;
        Assert.False(model.CanApply);
        Assert.Null(model.BuildRequest()); Assert.Contains("Select at least", model.Status);
        row.Included = true;
        Assert.True(model.CanApply);
        Assert.Equal(2, applyChanges);
        Assert.True(model.DeselectAllCommand.CanExecute(null));
        model.DeselectAllCommand.Execute(null);
        Assert.False(row.Included);
        Assert.False(model.CanApply);
        model.SelectAllCommand.Execute(null);
        Assert.True(row.Included);
        Assert.True(model.CanApply);
    }

    [Fact]
    public async Task StartRoutesTaggingAndMediaSelectionsDoNotSuggestConversion()
    {
        string path = await KaraokeTaggingTests.CreateMedia(".mp4");
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        model.SelectedOperation = model.Operations.Single(o => o.IsTagging);
        Assert.False(model.SelectedOperation.IsValidation);
        model.SetInputs([path]); Assert.True(model.SelectedOperation.IsTagging);
        Assert.True(model.CanChooseFiles); Assert.True(model.CanStart);
        bool called = false;
        model.ConfigureTaggingAsync = async dialog => { called = true; await dialog.PreviewAsync(); return dialog.BuildRequest(); };
        await model.StartCommand.ExecuteAsync(null);
        Assert.True(called); Assert.False(model.IsBusy); Assert.False(model.CanStart); Assert.True(model.ShowProgress);
        Assert.Equal("Tagging Report", model.LastReport!.ReportType);
        Assert.Contains("1 updated", model.Status);
    }
}
