using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Naming;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Tests.FileOperations;

public class FilenameRenameServiceTests
{
    private const string SourcePattern = "%DISC%-%TRACK% %TITLE% - %ARTIST%";
    private const string Before = "Hazz 20-02 I Can't Help But Wonder Where I'm Bound - Tom Paxton";
    private const string After = "HAZZ20-02 - Tom Paxton - I Can't Help But Wonder Where I'm Bound";

    [Fact]
    public async Task SelectedAudioPreviewsAndRenamesBothFilesWithoutChangingTheirContents()
    {
        string folder = TestMediaFixture.NewFolder();
        string audio = Write(folder, Before + ".MP3", "audio");
        string graphics = Write(folder, Before + ".cdg", "graphics");
        string unrelated = Write(folder, Before + ".zip", "other");
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        var service = new FilenameRenameService(store);
        var request = new FilenameRenameRequest([audio], SourcePattern, KaraokeFilenamePattern.DiscIdTrackArtistTitle);

        var plan = await service.PreviewAsync(request);
        Assert.True(plan.CanApply);
        Assert.Equal(new[] { After + ".cdg", After + ".MP3" }.Order(StringComparer.OrdinalIgnoreCase),
            plan.Items.Select(row => Path.GetFileName(row.Destination)).Order(StringComparer.OrdinalIgnoreCase));
        Assert.True(File.Exists(audio));
        Assert.True(File.Exists(graphics));

        var result = await service.ApplyAsync(request, plan, RecoveryVerificationMode.XxHash128, true);
        Assert.Equal(2, result.Succeeded);
        Assert.False(File.Exists(audio));
        Assert.False(File.Exists(graphics));
        Assert.Equal("audio", File.ReadAllText(Path.Combine(folder, After + ".MP3")));
        Assert.Equal("graphics", File.ReadAllText(Path.Combine(folder, After + ".cdg")));
        Assert.Equal("other", File.ReadAllText(unrelated));
        Assert.Empty(store.Find());
    }

    [Fact]
    public async Task UppercaseDiscCanBeTurnedOffIndependentlyOfRemovingWhitespace()
    {
        string folder = TestMediaFixture.NewFolder();
        string audio = Write(folder, Before + ".mp3", "audio");
        var service = new FilenameRenameService();
        var request = new FilenameRenameRequest([audio], SourcePattern, KaraokeFilenamePattern.DiscIdTrackArtistTitle,
            RemoveDiscWhitespace: true);

        Assert.True(request.UppercaseDisc);
        var uppercase = await service.PreviewAsync(request);
        Assert.Equal(After + ".mp3", Path.GetFileName(Assert.Single(uppercase.Items).Destination));
        var originalCase = await service.PreviewAsync(request with { UppercaseDisc = false });
        Assert.Equal("Hazz20-02 - Tom Paxton - I Can't Help But Wonder Where I'm Bound.mp3",
            Path.GetFileName(Assert.Single(originalCase.Items).Destination));
    }

    [Fact]
    public async Task UppercaseDiscCanPublishACaseOnlyRenameForAnAudioGraphicsPair()
    {
        string folder = TestMediaFixture.NewFolder();
        const string originalStem = "Abc20-02 - Artist - Title";
        const string renamedStem = "ABC20-02 - Artist - Title";
        string audio = Write(folder, originalStem + ".mp3", "audio");
        Write(folder, originalStem + ".cdg", "graphics");
        var service = new FilenameRenameService(new RecoveryStore(TestMediaFixture.NewFolder()));
        var request = new FilenameRenameRequest([audio], KaraokeFilenamePattern.DiscIdTrackArtistTitle,
            KaraokeFilenamePattern.DiscIdTrackArtistTitle);

        var plan = await service.PreviewAsync(request);
        Assert.True(plan.CanApply);
        var result = await service.ApplyAsync(request, plan, RecoveryVerificationMode.XxHash128, true);

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(new[] { renamedStem + ".cdg", renamedStem + ".mp3" },
            Directory.GetFiles(folder).Select(Path.GetFileName).Where(name => name != ".karaoke-run-lease").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ChangedPreviewAndAnOccupiedCompanionNameCannotPublish()
    {
        string folder = TestMediaFixture.NewFolder();
        string audio = Write(folder, Before + ".mp3", "audio");
        string graphics = Write(folder, Before + ".cdg", "graphics");
        var service = new FilenameRenameService(new RecoveryStore(TestMediaFixture.NewFolder()));
        var request = new FilenameRenameRequest([graphics], SourcePattern, KaraokeFilenamePattern.DiscIdTrackArtistTitle,
            RemoveDiscWhitespace: true);
        var approved = await service.PreviewAsync(request);

        string occupied = Write(folder, After + ".flac", "unrelated audio");
        var current = await service.PreviewAsync(request);
        Assert.False(current.CanApply);
        Assert.All(current.Items, item => Assert.True(item.IsConflict));
        var result = await service.ApplyAsync(request, approved, RecoveryVerificationMode.Sha256, true);
        Assert.Equal(2, result.Skipped);
        Assert.Equal("audio", File.ReadAllText(audio));
        Assert.Equal("graphics", File.ReadAllText(graphics));
        Assert.Equal("unrelated audio", File.ReadAllText(occupied));
        Assert.False(File.Exists(Path.Combine(folder, After + ".cdg")));
    }

    [Fact]
    public async Task DuplicateDestinationsAreBlockedAndMissingFieldsAreExplained()
    {
        string folder = TestMediaFixture.NewFolder();
        Write(folder, "ABC-01 - Artist - Title.zip", "first");
        Write(folder, "ABC-02 - Artist - Title.zip", "second");
        var service = new FilenameRenameService(new RecoveryStore(TestMediaFixture.NewFolder()));
        var request = new FilenameRenameRequest([folder], KaraokeFilenamePattern.DiscIdTrackArtistTitle, "%TITLE%");

        var duplicate = await service.PreviewAsync(request);
        Assert.False(duplicate.CanApply);
        Assert.All(duplicate.Items, item => Assert.True(item.IsConflict));

        var missing = await service.PreviewAsync(request with
        {
            SourcePattern = KaraokeFilenamePattern.DiscIdArtistTitle,
            DestinationPattern = KaraokeFilenamePattern.DiscIdTrackArtistTitle
        });
        Assert.False(missing.CanApply);
        Assert.All(missing.Items, item => Assert.Contains("does not supply %TRACK%", item.Issue));
    }

    [Fact]
    public async Task DestinationCompanionAddedDuringPreparationPreventsRename()
    {
        string folder = TestMediaFixture.NewFolder();
        string audio = Write(folder, Before + ".mp3", "audio");
        string graphics = Write(folder, Before + ".cdg", "graphics");
        var service = new FilenameRenameService(new RecoveryStore(TestMediaFixture.NewFolder()));
        var request = new FilenameRenameRequest([audio], SourcePattern, KaraokeFilenamePattern.DiscIdTrackArtistTitle,
            RemoveDiscWhitespace: true);
        var approved = await service.PreviewAsync(request);
        var progress = new SynchronousProgress<ConversionProgress>(update =>
        {
            if (update.IsIndeterminate)
                Write(folder, After + ".flac", "unrelated audio");
        });

        var result = await service.ApplyAsync(request, approved, RecoveryVerificationMode.Sha256, true, progress);
        Assert.Equal(2, result.Skipped);
        Assert.True(File.Exists(audio));
        Assert.True(File.Exists(graphics));
        Assert.False(File.Exists(Path.Combine(folder, After + ".cdg")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryCompletesAPreviouslyApprovedPartialPair(bool casingOnly)
    {
        string folder = TestMediaFixture.NewFolder();
        string oldStem = casingOnly ? "Abc20-02 - Artist - Title" : Before;
        string newStem = casingOnly ? "ABC20-02 - Artist - Title" : After;
        string audio = Write(folder, oldStem + ".mp3", "audio");
        string graphics = Write(folder, oldStem + ".cdg", "graphics");
        string newAudio = Path.Combine(folder, newStem + ".mp3");
        string newGraphics = Path.Combine(folder, newStem + ".cdg");
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        string journal;
        using (var session = new WorkflowSession(store, new WorkflowData
        {
            Roots = [folder],
            Kind = RecoveryKind.FileMoves,
            OperationId = OperationId.RenameByPattern,
            Moves = [new() { Items = [WorkflowMoves.Move(audio, newAudio), WorkflowMoves.Move(graphics, newGraphics)] }]
        }))
        {
            journal = session.Journal;
            session.BeginMoveGroup(0);
            GroupingRename.Move(audio, newAudio);
        }

        Assert.Equal(nameof(OperationId.RenameByPattern), Assert.Single(store.Find()).Action);
        var result = await new OperationExecutionService(store).ResumeAsync(journal);
        Assert.False(result.HasIssues);
        Assert.Equal("audio", File.ReadAllText(newAudio));
        Assert.Equal("graphics", File.ReadAllText(newGraphics));
        Assert.DoesNotContain(graphics, Directory.GetFiles(folder), StringComparer.Ordinal);
        Assert.Empty(store.Find());
    }

    private static string Write(string folder, string name, string value)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, value);
        return path;
    }
}
