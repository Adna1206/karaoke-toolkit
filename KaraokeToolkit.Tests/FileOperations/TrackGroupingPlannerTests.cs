using KaraokeToolkit.Core.FileOperations;

namespace KaraokeToolkit.Tests.FileOperations;

public class TrackGroupingPlannerTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("_")]
    public void RangeOriginSeparatesBoundaryTracksAndKeepsCompanionsTogether(string inputSeparator)
    {
        string root = TestMediaFixture.NewFolder(), output = Path.Combine(root, "sorted");
        string previous = FileAt(root, "BKD-33600 - Artist - Title.zip");
        string audio = FileAt(root, $"BKD{inputSeparator}33601 - Artist - Title.mp3");
        string graphics = FileAt(root, $"BKD{inputSeparator}33601 - Artist - Title.cdg");
        string otherPrefix = FileAt(root, "SF-33652.zip");

        var plan = TrackGroupingPlanner.Plan([previous, audio, otherPrefix], output, new()
        {
            RangeSize = 50,
            RangeSeparator = " to "
        });

        Assert.Empty(plan.Issues);
        Assert.Equal(4, plan.Items.Count);
        Assert.Equal("BKD-33551 to BKD-33600", ParentOf(plan, previous));
        Assert.Equal("BKD-33601 to BKD-33650", ParentOf(plan, audio));
        Assert.Equal(ParentOf(plan, audio), ParentOf(plan, graphics));
        Assert.Equal("SF-33651 to SF-33700", ParentOf(plan, otherPrefix));
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData(0, "ABC-3xx")]
    [InlineData(50, "ABC-351 to ABC-400")]
    public void TrackSuffixesUseNumericBucketAndKeepCompleteFilenames(int rangeSize, string group)
    {
        string root = TestMediaFixture.NewFolder(), output = Path.Combine(root, "sorted");
        string audio = FileAt(root, "ABC-369A - Artist (WVocal) - Title (Ext Ver).mp3");
        string graphics = FileAt(root, "ABC-369A - Artist (WVocal) - Title (Ext Ver).cdg");
        string variant = FileAt(root, "ABC369bc.zip");
        string numeric = FileAt(root, "ABC-369.zip");

        var plan = TrackGroupingPlanner.Plan([audio, variant, numeric], output, new()
        {
            RangeSize = rangeSize,
            RangeSeparator = " to "
        });

        Assert.Empty(plan.Issues);
        Assert.Equal(4, plan.Items.Count);
        foreach (string source in new[] { audio, graphics, variant, numeric })
            Assert.Equal(Path.Combine(output, group, Path.GetFileName(source)),
                plan.Items.Single(item => item.Source == source).Destination);
    }

    [Theory]
    [InlineData(0, "ABC-003xx")]
    [InlineData(100, "ABC-00300 to ABC-00399")]
    public void MinimumIdDigitsUnifiesSourceWidthsInBothModes(int rangeSize, string group)
    {
        string root = TestMediaFixture.NewFolder(), output = Path.Combine(root, "sorted");
        string audio = FileAt(root, "ABC-369A.mp3");
        string graphics = FileAt(root, "ABC-369A.cdg");
        string variant = FileAt(root, "ABC-000369.zip");
        var options = new TrackGroupingOptions { RangeSize = rangeSize, RangeStart = 0, RangeSeparator = " to " };

        var sourceWidth = TrackGroupingPlanner.Plan([audio, variant], output, options);
        Assert.NotEqual(ParentOf(sourceWidth, audio), ParentOf(sourceWidth, variant));

        var padded = TrackGroupingPlanner.Plan([audio, variant], output, options with { MinimumIdDigits = 5 });
        Assert.Empty(padded.Issues);
        Assert.Equal(3, padded.Items.Count);
        foreach (string source in new[] { audio, graphics, variant })
            Assert.Equal(Path.Combine(output, group, Path.GetFileName(source)),
                padded.Items.Single(item => item.Source == source).Destination);
    }

    [Fact]
    public void MinimumIdDigitsPadsAutomaticParentsWithoutAddingLevels()
    {
        string root = TestMediaFixture.NewFolder(), output = Path.Combine(root, "sorted");
        string shortId = FileAt(root, "ABC-369.zip");
        string longerId = FileAt(root, "ABC-1369.zip");
        string zeroPadded = FileAt(root, "ABC-00001369.zip");
        var options = new TrackGroupingOptions
        {
            MinimumIdDigits = 6, CreateParents = true, SkipSingleChildParents = false
        };

        var plan = TrackGroupingPlanner.Plan([shortId, longerId, zeroPadded], output, options);

        Assert.Empty(plan.Issues);
        Assert.Equal("ABC-0003xx", RelativeParent(plan, shortId, output));
        Assert.Equal(Path.Combine("ABC-001xxx", "ABC-0013xx"), RelativeParent(plan, longerId, output));
        Assert.Equal(RelativeParent(plan, longerId, output), RelativeParent(plan, zeroPadded, output));
    }

    [Fact]
    public void MinimumIdDigitsKeepsLongerRangeEndpoints()
    {
        string root = TestMediaFixture.NewFolder(), output = Path.Combine(root, "sorted");
        string source = FileAt(root, "ABC-99999.zip");
        var options = new TrackGroupingOptions { RangeSize = 100, MinimumIdDigits = 5, RangeSeparator = " to " };

        var plan = TrackGroupingPlanner.Plan([source], output, options);

        Assert.Equal("ABC-99901 to ABC-100000", ParentOf(plan, source));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackGroupingPlanner.Plan([source], output, options with { MinimumIdDigits = 10 }));
    }

    [Fact]
    public void WholeIntervalFoldersStayDistinctByPrefixAndMustFitTheirDestination()
    {
        string root = TestMediaFixture.NewFolder(), output = Path.Combine(root, "sorted");
        string first = Directory.CreateDirectory(Path.Combine(root, "BKD-31xxx")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(root, "BK-32###")).FullName;
        string spanning = Directory.CreateDirectory(Path.Combine(root, "BKD-39000 to BKD-41000")).FullName;

        var plan = TrackGroupingPlanner.Plan([first, second, spanning], output, new()
        {
            WholeFolders = true,
            TrailingDigits = 4
        });

        Assert.Equal("BKD-3xxxx", ParentOf(plan, first));
        Assert.Equal("BK-3xxxx", ParentOf(plan, second));
        Assert.All(plan.Items, i => Assert.True(i.IsDirectory));
        Assert.Equal(spanning, Assert.Single(plan.Issues).Source);
    }

    [Fact]
    public void WholeFoldersAcceptExplicitCustomRangesAndSkipAmbiguousStartOnlyLabels()
    {
        string root = TestMediaFixture.NewFolder(), output = Path.Combine(root, "sorted");
        string range = Directory.CreateDirectory(Path.Combine(root, "BKD-33601 through BKD-33650")).FullName;
        string ambiguous = Directory.CreateDirectory(Path.Combine(root, "BKD-33600s")).FullName;
        string variant = Directory.CreateDirectory(Path.Combine(root, "BKD-33601A")).FullName;
        string mixed = Directory.CreateDirectory(Path.Combine(root, "BKD-33601 to SF-33650")).FullName;
        var plan = TrackGroupingPlanner.Plan([range, ambiguous, variant, mixed], output, new()
        {
            WholeFolders = true,
            RangeSize = 100,
            RangeSeparator = " through "
        });

        Assert.Equal("BKD-33601 through BKD-33700", ParentOf(plan, range));
        Assert.Equal(ParentOf(plan, range), ParentOf(plan, variant));
        Assert.Equal(2, plan.Issues.Count);
        Assert.Contains(plan.Issues, issue => issue.Source == ambiguous && !issue.IsConflict);
        Assert.Contains(plan.Issues, issue => issue.Source == mixed && !issue.IsConflict);
    }

    [Fact]
    public void AutomaticParentsHonorDepthAndSingleChildPruning()
    {
        string root = TestMediaFixture.NewFolder(), output = Path.Combine(root, "sorted");
        string file = FileAt(root, "ABC-123456.zip");
        string neighbor = FileAt(root, "ABC-123567.zip");
        string otherBranch = FileAt(root, "ABC-789012.zip");
        string[] files = [file, neighbor, otherBranch];
        TrackGroupingOptions options = new() { CreateParents = true, SkipSingleChildParents = false };

        var full = TrackGroupingPlanner.Plan(files, output, options);
        Assert.Equal(Path.Combine("ABC-1xxxxx", "ABC-12xxxx", "ABC-123xxx", "ABC-1234xx"), RelativeParent(full, file, output));
        Assert.Equal(Path.Combine("ABC-7xxxxx", "ABC-78xxxx", "ABC-789xxx", "ABC-7890xx"), RelativeParent(full, otherBranch, output));

        var limited = TrackGroupingPlanner.Plan(files, output, options with { MaxParentLevels = 1 });
        Assert.Equal(Path.Combine("ABC-123xxx", "ABC-1234xx"), RelativeParent(limited, file, output));

        var pruned = TrackGroupingPlanner.Plan(files, output, options with { SkipSingleChildParents = true });
        Assert.Equal(RelativeParent(limited, file, output), RelativeParent(pruned, file, output));
        Assert.Equal("ABC-7890xx", RelativeParent(pruned, otherBranch, output));
    }

    [Fact]
    public void RecursiveScanSkipsItsOutputAndReportsConflictingPaths()
    {
        string root = TestMediaFixture.NewFolder();
        string output = Directory.CreateDirectory(Path.Combine(root, "sorted")).FullName;
        string bucket = Directory.CreateDirectory(Path.Combine(output, "BKD-336xx")).FullName;
        string source = FileAt(root, "BKD33601.zip");
        FileAt(bucket, "BKD33601.zip");
        FileAt(bucket, "BKD33602.zip");
        string unmatched = FileAt(root, "readme.txt");

        var plan = TrackGroupingPlanner.Plan([root], output, new() { Recurse = true });

        Assert.Equal(source, Assert.Single(plan.Items).Source);
        Assert.Contains(plan.Issues, issue => issue.Source == source && issue.IsConflict);
        Assert.Contains(plan.Issues, issue => issue.Source == unmatched && !issue.IsConflict);
    }

    private static string FileAt(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, name);
        return path;
    }

    private static string ParentOf(TrackGroupingPlan plan, string source) =>
        Path.GetFileName(Path.GetDirectoryName(plan.Items.Single(i => i.Source == source).Destination)!);

    private static string RelativeParent(TrackGroupingPlan plan, string source, string output) =>
        Path.GetRelativePath(output, Path.GetDirectoryName(plan.Items.Single(i => i.Source == source).Destination)!);
}
