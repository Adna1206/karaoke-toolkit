using KaraokeToolkit.App.Features.Main;

namespace KaraokeToolkit.Tests.App.Main;

public class ProgressLogScrollTests
{
    [Fact]
    public void NewEntriesDoNotDisableFollowingAndUserScrollPausesUntilBottom()
    {
        var state = new LogScrollState();
        state.Observe(100, 100, layoutChanged: false);
        state.Observe(100, 150, layoutChanged: true);
        Assert.True(state.Following);
        state.Observe(150, 150, layoutChanged: false);
        state.Observe(75, 150, layoutChanged: false);
        Assert.False(state.Following);
        state.Observe(75, 200, layoutChanged: true);
        Assert.False(state.Following);
        state.Observe(175, 200, layoutChanged: false);
        Assert.False(state.Following);
        state.Observe(200, 200, layoutChanged: false);
        Assert.True(state.Following);
        state.Observe(200, 250, layoutChanged: true);
        Assert.True(state.Following);
    }

    [Fact]
    public void UpwardWheelIntentPausesFollowingBeforeLayoutChanges()
    {
        var state = new LogScrollState();
        state.Pause();

        state.Observe(100, 140, layoutChanged: true);
        Assert.False(state.Following);

        state.Observe(140, 140, layoutChanged: false);
        Assert.True(state.Following);
    }

    [Fact]
    public void NewRunResumesFollowingAfterReadingEarlierEntries()
    {
        var state = new LogScrollState();
        state.Observe(50, 200, layoutChanged: false);
        Assert.False(state.Following);
        state.Reset();
        Assert.True(state.Following);
        state.Observe(0, 0, layoutChanged: true);
        Assert.True(state.Following);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(99.5, 100, true)]
    [InlineData(95, 100, false)]
    public void HandlesShortLogsAndFractionalOffsets(double offset, double maximum, bool expected)
    {
        var state = new LogScrollState();
        state.Observe(offset, maximum, layoutChanged: false);
        Assert.Equal(expected, state.Following);
    }
}
