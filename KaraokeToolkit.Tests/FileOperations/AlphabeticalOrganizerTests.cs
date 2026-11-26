using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Tests.FileOperations;

public class AlphabeticalOrganizerTests
{
    [Fact]
    public async Task MultipleFoldersOrganizeImmediateChildFoldersWithoutMovingExistingBuckets()
    {
        string first = TestMediaFixture.NewFolder(), second = TestMediaFixture.NewFolder();
        Directory.CreateDirectory(Path.Combine(first, "A"));
        File.WriteAllText(Path.Combine(first, "A", "old.txt"), "old");
        Directory.CreateDirectory(Path.Combine(first, "Bravo"));
        Directory.CreateDirectory(Path.Combine(first, "Bravo", "Nested"));
        Directory.CreateDirectory(Path.Combine(second, "3-song"));
        Directory.CreateDirectory(Path.Combine(second, "Zebra"));
        File.WriteAllText(Path.Combine(first, "apple.txt"), "apple");

        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "options.xmls"));
        model.SelectedOperation = model.Operations.Single(x => x.IsOrganizer);
        model.SetInputs([first, second]);
        Assert.Equal(2, model.CurrentInputs.Length);
        model.ConfigureAlphabeticalGroupingAsync = async preview =>
        {
            await preview.RefreshAsync();
            File.WriteAllText(Path.Combine(first, "The Example.txt"), "article");
            preview.IgnoreLeadingThe = true;
            Assert.DoesNotContain(preview.Moves, row => row.SourceName == "The Example.txt");
            await preview.RefreshAsync();
            Assert.Contains(preview.Moves, row => row.SourceName == "The Example.txt" && row.TargetName == Path.Combine("E", "The Example.txt"));
            preview.ScopeIndex = (int)AlphabeticalOrganizeScope.Folders;
            Assert.Null(preview.BuildSubmission());
            await preview.PendingPreview;
            Assert.NotNull(preview.BuildSubmission());
            Assert.Equal(3, preview.Moves.Count);
            Assert.True(Directory.Exists(Path.Combine(first, "Bravo")));
            return preview.BuildSubmission();
        };
        await model.StartCommand.ExecuteAsync(null);

        Assert.Equal(3, model.LastReport!.Entries.Count(x => x.Status == "Succeeded"));
        Assert.True(File.Exists(Path.Combine(first, "A", "old.txt")));
        Assert.True(File.Exists(Path.Combine(first, "apple.txt")));
        Assert.True(Directory.Exists(Path.Combine(first, "B", "Bravo")));
        Assert.True(Directory.Exists(Path.Combine(first, "B", "Bravo", "Nested")));
        Assert.False(Directory.Exists(Path.Combine(first, "B", "Bravo", "N")));
        Assert.True(Directory.Exists(Path.Combine(second, "#", "3-song")));
        Assert.True(Directory.Exists(Path.Combine(second, "Z", "Zebra")));
        Assert.False(model.CanStart);
    }

    [Fact]
    public async Task FileOperationRecursesButDoesNotReprocessExistingBuckets()
    {
        string root = TestMediaFixture.NewFolder();
        File.WriteAllText(Path.Combine(root, "song.txt"), "song");
        Directory.CreateDirectory(Path.Combine(root, "Band"));
        File.WriteAllText(Path.Combine(root, "Band", "track.mp3"), "audio");
        Directory.CreateDirectory(Path.Combine(root, "A"));
        File.WriteAllText(Path.Combine(root, "A", "old.txt"), "old");
        var result = await new AlphabeticalOrganizer().RunAsync([root], AlphabeticalOrganizeScope.Files);
        Assert.Equal(2, result.Succeeded);
        Assert.True(File.Exists(Path.Combine(root, "S", "song.txt")));
        Assert.True(File.Exists(Path.Combine(root, "Band", "T", "track.mp3")));
        Assert.True(File.Exists(Path.Combine(root, "A", "old.txt")));
        Assert.True(Directory.Exists(Path.Combine(root, "Band")));
    }

    [Fact]
    public async Task SelectedAudioCarriesCdgAndCollisionLeavesBothInPlace()
    {
        string root = TestMediaFixture.NewFolder();
        string audio = Path.Combine(root, "song.mp3"), cdg = Path.Combine(root, "song.cdg");
        File.WriteAllText(audio, "audio");
        File.WriteAllText(cdg, "graphics");
        Directory.CreateDirectory(Path.Combine(root, "S"));
        File.WriteAllText(Path.Combine(root, "S", "song.cdg"), "existing");
        var organizer = new AlphabeticalOrganizer();
        var blocked = await organizer.RunAsync([audio], AlphabeticalOrganizeScope.Files);
        Assert.Equal(2, blocked.Skipped);
        Assert.True(File.Exists(audio));
        Assert.True(File.Exists(cdg));
        File.Delete(Path.Combine(root, "S", "song.cdg"));
        var moved = await organizer.RunAsync([audio], AlphabeticalOrganizeScope.Files);
        Assert.Equal(2, moved.Succeeded);
        Assert.True(File.Exists(Path.Combine(root, "S", "song.mp3")));
        Assert.True(File.Exists(Path.Combine(root, "S", "song.cdg")));
    }

    [Fact]
    public async Task PreviewAndApplySkipTheWholeConflictingPairAndMoveOtherFiles()
    {
        string root = TestMediaFixture.NewFolder();
        string audio = Path.Combine(root, "song.mp3");
        string graphics = Path.Combine(root, "song.cdg");
        string notes = Path.Combine(root, "notes.txt");
        File.WriteAllText(audio, "audio");
        File.WriteAllText(graphics, "graphics");
        File.WriteAllText(notes, "notes");
        Directory.CreateDirectory(Path.Combine(root, "S"));
        File.WriteAllText(Path.Combine(root, "S", "song.cdg"), "occupied");
        var organizer = new AlphabeticalOrganizer();

        var plan = await organizer.PreviewAsync([root], AlphabeticalOrganizeScope.Files);
        Assert.Equal(new[] { audio, graphics }, plan.Files.Where(file => file.Status == ConversionStatus.Failed)
            .Select(file => file.Source).OrderDescending(StringComparer.Ordinal));
        Assert.Equal(notes, Assert.Single(plan.Files, file => file.Status == ConversionStatus.Succeeded).Source);
        var result = await organizer.ApplyPreviewAsync([root], AlphabeticalOrganizeScope.Files, plan);

        Assert.Equal(2, result.Skipped);
        Assert.Equal("audio", File.ReadAllText(audio));
        Assert.Equal("graphics", File.ReadAllText(graphics));
        Assert.Equal("notes", File.ReadAllText(Path.Combine(root, "N", "notes.txt")));
    }

    [Fact]
    public async Task ApprovalSkipsAnOccupiedDestinationAndIgnoresNewFiles()
    {
        string root = TestMediaFixture.NewFolder();
        string audio = Path.Combine(root, "song.mp3");
        string notes = Path.Combine(root, "notes.txt");
        File.WriteAllText(audio, "audio");
        File.WriteAllText(notes, "notes");
        var organizer = new AlphabeticalOrganizer();
        var approved = await organizer.PreviewAsync([root], AlphabeticalOrganizeScope.Files);
        string target = Assert.Single(Assert.Single(approved.Files, row => row.Source == audio).Outputs);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "new destination");
        string added = Path.Combine(root, "later.txt");
        File.WriteAllText(added, "later");

        var result = await organizer.ApplyPreviewAsync([root], AlphabeticalOrganizeScope.Files, approved);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal("audio", File.ReadAllText(audio));
        Assert.Equal("new destination", File.ReadAllText(target));
        Assert.Equal("notes", File.ReadAllText(Path.Combine(root, "N", "notes.txt")));
        Assert.True(File.Exists(added));
    }

    [Fact]
    public async Task ExplicitFileSelectionDoesNotMoveNeighbours()
    {
        string root = TestMediaFixture.NewFolder();
        string chosen = Path.Combine(root, "alpha.txt"), other = Path.Combine(root, "beta.txt");
        File.WriteAllText(chosen, "a");
        File.WriteAllText(other, "b");
        var result = await new AlphabeticalOrganizer().RunAsync([chosen], AlphabeticalOrganizeScope.Files);
        Assert.Equal(1, result.Succeeded);
        Assert.True(File.Exists(Path.Combine(root, "A", "alpha.txt")));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public async Task FolderOperationRejectsFiles()
    {
        string root = TestMediaFixture.NewFolder();
        string file = Path.Combine(root, "song.txt");
        File.WriteAllText(file, "song");
        await Assert.ThrowsAsync<ArgumentException>(() => new AlphabeticalOrganizer().RunAsync([file], AlphabeticalOrganizeScope.Folders));
        Assert.True(File.Exists(file));
    }

    [Theory]
    [InlineData(false, "T")]
    [InlineData(true, "B")]
    public async Task LeadingTheOptionChangesOnlyTheLetterFolder(bool ignoreLeadingThe, string bucket)
    {
        string root = TestMediaFixture.NewFolder();
        File.WriteAllText(Path.Combine(root, "The Beatles.mp3"), "audio");
        File.WriteAllText(Path.Combine(root, "The Beatles.cdg"), "graphics");
        File.WriteAllText(Path.Combine(root, "the Rememberants.txt"), "text");
        File.WriteAllText(Path.Combine(root, "The.txt"), "article only");
        var result = await new AlphabeticalOrganizer().RunAsync([root], AlphabeticalOrganizeScope.Files,
            ignoreLeadingThe: ignoreLeadingThe);
        Assert.Equal(4, result.Succeeded);
        Assert.True(File.Exists(Path.Combine(root, bucket, "The Beatles.mp3")));
        Assert.True(File.Exists(Path.Combine(root, bucket, "The Beatles.cdg")));
        Assert.True(File.Exists(Path.Combine(root, ignoreLeadingThe ? "R" : "T", "the Rememberants.txt")));
        Assert.True(File.Exists(Path.Combine(root, "T", "The.txt")));
    }

    [Fact]
    public async Task SettingIsOffByDefaultPersistsAndDoesNotChangeFolderSorting()
    {
        string settings = Path.Combine(TestMediaFixture.NewFolder(), "options.xmls");
        var model = new MainViewModel(settings);
        Assert.False(model.IgnoreLeadingTheForFileOrganization);
        model.SaveOptions(model.Options, model.OverwritePolicy, model.Theme, model.ValidateBeforeConversion, true);
        Assert.True(new SettingsStore(settings).LoadIgnoreLeadingTheForFileOrganization());
        Assert.True(new MainViewModel(settings).IgnoreLeadingTheForFileOrganization);

        string root = TestMediaFixture.NewFolder();
        Directory.CreateDirectory(Path.Combine(root, "The Beatles"));
        var result = await new AlphabeticalOrganizer().RunAsync([root], AlphabeticalOrganizeScope.Folders,
            ignoreLeadingThe: true);
        Assert.Equal(1, result.Succeeded);
        Assert.True(Directory.Exists(Path.Combine(root, "T", "The Beatles")));
    }

    [Theory]
    [InlineData("ABC-0000 - Freddie Hart - Song.zip", " - ", false, "F")]
    [InlineData("ABC123 - Freddie Hart - Song.zip", " - ", false, "F")]
    [InlineData("ABC-1234. Freddie Hart.zip", ". ", false, "F")]
    [InlineData("ABC-1234 – Freddie Hart.zip", " – ", false, "F")]
    [InlineData("ABC-1234_Freddie Hart.zip", "_", false, "F")]
    [InlineData("ABC_1234 _ Freddie Hart.zip", " _ ", false, "F")]
    [InlineData("ABC 1234   Freddie Hart.zip", " ", false, "F")]
    [InlineData("ABC-1234-Freddie Hart.zip", "-", false, "F")]
    [InlineData("The Beatles - Song.zip", " - ", false, "T")]
    [InlineData("ABC-1234 Bonus - Freddie Hart.zip", " - ", false, "A")]
    [InlineData("ABC-1234. Freddie Hart.zip", " - ", false, "A")]
    [InlineData("ABC-1234 - .zip", " - ", false, "A")]
    [InlineData("ABC-1234 - The Beatles.zip", " - ", true, "B")]
    public async Task OnlyCompleteLeadingTrackIdsAreSkipped(string name, string separator, bool ignoreThe, string bucket)
    {
        string root = TestMediaFixture.NewFolder();
        string source = Path.Combine(root, name);
        File.WriteAllText(source, "media");
        var organizer = new AlphabeticalOrganizer();
        var snapshot = await organizer.ReadPreviewAsync([source], CancellationToken.None);
        var plan = snapshot.Plan(AlphabeticalOrganizeScope.Files, ignoreThe, separator);
        Assert.Equal(Path.Combine(root, bucket, name), Assert.Single(Assert.Single(plan.Files).Outputs));
    }

    [Fact]
    public async Task DesktopTrackIdOptionUpdatesThePreviewAndMovesBothPartsOfAKaraokePair()
    {
        string root = TestMediaFixture.NewFolder();
        string stem = "ABC-0000. Freddie Hart - Song";
        string audio = Path.Combine(root, stem + ".mp3");
        string graphics = Path.Combine(root, stem + ".cdg");
        File.WriteAllText(audio, "audio");
        File.WriteAllText(graphics, "graphics");
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        model.SelectedOperation = model.Operations.Single(operation => operation.IsOrganizer);
        model.SetInputs([audio]);
        model.ConfigureAlphabeticalGroupingAsync = async preview =>
        {
            await preview.RefreshAsync();
            Assert.All(preview.Moves, row => Assert.StartsWith($"A{Path.DirectorySeparatorChar}", row.TargetName));
            preview.SkipLeadingTrackId = true;
            preview.TrackIdSeparator = "";
            await preview.PendingPreview;
            Assert.Null(preview.BuildSubmission());
            Assert.Contains("Enter a separator", preview.Status);

            preview.TrackIdSeparator = ". ";
            await preview.PendingPreview;
            Assert.All(preview.Moves, row => Assert.StartsWith($"F{Path.DirectorySeparatorChar}", row.TargetName));
            preview.SkipLeadingTrackId = false;
            await preview.PendingPreview;
            Assert.All(preview.Moves, row => Assert.StartsWith($"A{Path.DirectorySeparatorChar}", row.TargetName));
            preview.SkipLeadingTrackId = true;
            await preview.PendingPreview;
            Assert.Equal(". ", preview.TrackIdSeparator);
            Assert.Equal(2, preview.Moves.Count);
            return preview.BuildSubmission();
        };
        await model.StartCommand.ExecuteAsync(null);

        Assert.Equal("audio", File.ReadAllText(Path.Combine(root, "F", Path.GetFileName(audio))));
        Assert.Equal("graphics", File.ReadAllText(Path.Combine(root, "F", Path.GetFileName(graphics))));
        Assert.False(File.Exists(audio));
        Assert.False(File.Exists(graphics));
    }
}
