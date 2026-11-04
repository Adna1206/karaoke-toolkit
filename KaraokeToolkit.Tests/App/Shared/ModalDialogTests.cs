using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.Tests.App.Shared;

public class ModalDialogTests
{
    [Fact]
    public async Task OwnerRemainsDimmedUntilDialogCompletesAndResultIsPreserved()
    {
        bool dimmed = false;
        var overlay = new ModalOverlay(() => dimmed = true, () => dimmed = false);
        var completed = new TaskCompletionSource<int>();
        var dialog = overlay.RunAsync(() => completed.Task);
        Assert.True(dimmed);
        Assert.False(dialog.IsCompleted);
        completed.SetResult(42);
        Assert.Equal(42, await dialog);
        Assert.False(dimmed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOrCancellationAlwaysRemovesSmoke(bool cancelled)
    {
        bool dimmed = false;
        var overlay = new ModalOverlay(() => dimmed = true, () => dimmed = false);
        Func<Task> action = cancelled
            ? () => Task.FromCanceled(new CancellationToken(true))
            : () => throw new InvalidOperationException("Could not open dialog");
        if (cancelled) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => overlay.RunAsync(action));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => overlay.RunAsync(action));
        Assert.False(dimmed);
        await overlay.RunAsync(() => { Assert.True(dimmed); return Task.CompletedTask; });
        Assert.False(dimmed);
    }

    [Fact]
    public async Task OverlappingDialogsShareSmokeUntilTheLastCloses()
    {
        int shows = 0, hides = 0;
        var overlay = new ModalOverlay(() => shows++, () => hides++);
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var firstDialog = overlay.RunAsync(() => first.Task);
        var secondDialog = overlay.RunAsync(() => second.Task);
        Assert.Equal(1, shows);
        first.SetResult();
        await firstDialog;
        Assert.Equal(0, hides);
        second.SetResult();
        await secondDialog;
        Assert.Equal(1, hides);
    }

    [Fact]
    public async Task NestedDialogUndimsItsParentWithoutUndimmingMainWindow()
    {
        bool mainDimmed = false, settingsDimmed = false;
        var main = new ModalOverlay(() => mainDimmed = true, () => mainDimmed = false);
        var settings = new ModalOverlay(() => settingsDimmed = true, () => settingsDimmed = false);
        await main.RunAsync(async () =>
        {
            await settings.RunAsync(() =>
            {
                Assert.True(mainDimmed);
                Assert.True(settingsDimmed);
                return Task.CompletedTask;
            });
            Assert.True(mainDimmed);
            Assert.False(settingsDimmed);
        });
        Assert.False(mainDimmed);
    }

    [Fact]
    public async Task OverlaySetupFailureAlsoCleansUp()
    {
        bool cleaned = false, opened = false;
        var overlay = new ModalOverlay(() => throw new InvalidOperationException(), () => cleaned = true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => overlay.RunAsync(() =>
        {
            opened = true;
            return Task.CompletedTask;
        }));
        Assert.True(cleaned);
        Assert.False(opened);
    }
}
