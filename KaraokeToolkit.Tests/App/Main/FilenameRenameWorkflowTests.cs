using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Features.FilenameRename;
using KaraokeToolkit.Core.Naming;

namespace KaraokeToolkit.Tests.App.Main;

public class FilenameRenameWorkflowTests
{
    [Fact]
    public async Task PreviewSeparatesProposedNamesFromConflictsAndSkippedItems()
    {
        string folder = TestMediaFixture.NewFolder();
        string ready = Write("ABC 20-02 Song - Artist.zip");
        string conflict = Write("ABC 20-03 Other Song - Artist.zip");
        string skipped = Write("Unrecognized.zip");
        string occupied = Write("ABC20-03 - Artist - Other Song.zip");
        var preview = new FilenameRenameViewModel([ready, conflict, skipped], false, KaraokeFilenamePattern.Presets)
        {
            SourcePattern = "%DISC%-%TRACK% %TITLE% - %ARTIST%"
        };

        Assert.True(preview.RemoveDiscWhitespace);
        await preview.RefreshAsync();

        var change = Assert.Single(preview.Changes);
        Assert.Equal("ABC 20-02 Song - Artist.zip", change.SourceName);
        Assert.Equal("ABC20-02 - Artist - Song.zip", change.DestinationName);
        Assert.Collection(preview.Issues,
            row => Assert.Equal("Conflict · ABC 20-03 Other Song - Artist.zip", row.IssueHeading),
            row => Assert.Equal("Skipped · Unrecognized.zip", row.IssueHeading));
        Assert.True(preview.HasIssues);
        Assert.False(preview.CanApply);

        File.Delete(occupied);
        await preview.RefreshAsync();
        Assert.Equal(2, preview.Changes.Count);
        Assert.Single(preview.Issues);
        Assert.True(preview.CanApply);

        preview.RemoveDiscWhitespace = false;
        Assert.Empty(preview.Changes);
        Assert.False(preview.HasIssues);
        Assert.False(preview.CanApply);
        await preview.RefreshAsync();
        Assert.Contains(preview.Changes, row => row.DestinationName == "ABC 20-02 - Artist - Song.zip");

        string Write(string name)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, "media");
            return path;
        }
    }

    [Fact]
    public async Task StartUsesRenamePreviewAndRequiresARefreshAfterEdits()
    {
        string folder = TestMediaFixture.NewFolder();
        string source = Path.Combine(folder, "ABC 20-02 Song - Artist.zip");
        File.WriteAllText(source, "media");
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        var operation = model.Operations.Single(item => item.IsFilenameRename);
        model.SelectedOperation = operation;
        model.SetInputs([source]);
        Assert.Same(operation, model.SelectedOperation);

        bool stalePlanWasRejected = false;
        model.ConfigureFilenameRenameAsync = async preview =>
        {
            preview.SourcePattern = "%DISC%-%TRACK% %TITLE% - %ARTIST%";
            await preview.RefreshAsync();
            preview.RemoveDiscWhitespace = false;
            stalePlanWasRejected = preview.BuildSubmission() == null;
            preview.RemoveDiscWhitespace = true;
            await preview.RefreshAsync();
            return preview.BuildSubmission();
        };
        await model.StartCommand.ExecuteAsync(null);

        Assert.True(stalePlanWasRejected);
        Assert.Equal("Filename Rename Report", model.LastReport?.ReportType);
        Assert.Equal("media", File.ReadAllText(Path.Combine(folder, "ABC20-02 - Artist - Song.zip")));
        Assert.False(File.Exists(source));
    }
}
