using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.Recovery;

public class ValidationRecoveryTests
{

    [Fact]
    public async Task RecoveryKeepsSavedValidationPreference()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.ZipToBin, "keep");
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()
        {
            AutoCreateCueText = false
        }), OverwritePolicy.Fail, [Path.Combine(folder, "song.zip")], true))
        {
        }
        var messages = new List<string>();
        var result = await new ConversionService(store).ResumeAsync(Assert.Single(store.Find()), new SelectionConversionService.InlineProgress(p => messages.Add(p.Message)));
        Assert.Equal(1, result.Succeeded);
        Assert.Contains(messages, m => m.StartsWith("Validating input:"));
    }
}
