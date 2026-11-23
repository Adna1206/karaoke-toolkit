using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Features.Main.Dialogs;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;

namespace KaraokeToolkit.Tests.App.Main;

public class OutputConflictReviewTests
{
    [Fact]
    public void BulkChoicesCanBeOverriddenPerGroup()
    {
        var model = new OutputConflictReviewModel([new("one", ["one.zip"]), new("two", ["two.zip"])]);
        Assert.Equal(new[] { ConflictDecision.Skip, ConflictDecision.Skip }, model.GetChoices());
        model.SetAll(true);
        model.Rows[1].Replace = false;
        Assert.Equal(new[] { ConflictDecision.Replace, ConflictDecision.Skip }, model.GetChoices());
        Assert.Equal("1 replace · 1 skip", model.Summary);
        model.SetAll(false);
        Assert.False(model.HasReplacements);
    }

    [Fact]
    public async Task DesktopStartUsesOneReviewForTwoConflicts()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, OperationId.AudioGToBin, "keep");
        foreach (var extension in new[] { ".cdg", ".wav" })
            File.Copy(Path.Combine(folder, "song" + extension), Path.Combine(folder, "other" + extension));
        File.WriteAllText(Path.Combine(folder, "song.zip"), "existing song");
        File.WriteAllText(Path.Combine(folder, "other.zip"), "existing other");
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        model.SelectedOperation = model.Operations.Single(choice => choice.Action == OperationId.AudioGToZip);
        model.SetInputs([folder]);
        model.SaveOptions(model.Options with { AutoCreateCueText = false }, OverwritePolicy.Ask);
        var reviews = new List<IReadOnlyList<OutputConflict>>();
        model.ResolveConflictsAsync = (conflicts, _) =>
        {
            reviews.Add(conflicts);
            var choices = new OutputConflictReviewModel(conflicts);
            foreach (var row in choices.Rows)
                row.Replace = row.Name == "song.cdg";
            return Task.FromResult<IReadOnlyList<ConflictDecision>?>(choices.GetChoices());
        };

        await model.StartCommand.ExecuteAsync(null);

        Assert.Equal(2, Assert.Single(reviews).Count);
        Assert.Contains(model.LastReport!.Entries, entry => entry.Status == "Succeeded" && Path.GetFileName(entry.Source) == "song.cdg");
        Assert.Contains(model.LastReport.Entries, entry => entry.Status == "Skipped" && Path.GetFileName(entry.Source) == "other.cdg");
        Assert.Equal("existing other", File.ReadAllText(Path.Combine(folder, "other.zip")));
    }
}
