using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Core.Tests.FileOperations;

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
        model.SelectedOperation = model.Operations.Single(x => x.OrganizeMode == AlphabeticalOrganizeScope.Folders);
        model.SetInputs([first, second]);
        Assert.Equal(2, model.CurrentInputs.Length);
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
}
