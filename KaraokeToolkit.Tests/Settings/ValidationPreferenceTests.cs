using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.Settings;

public class ValidationPreferenceTests
{
    private static string Settings() => Path.Combine(TestMediaFixture.NewFolder(), "options.xmls");
    [Fact]
    public void ValidationPreferenceDefaultsOffAndSurvivesOtherOptionSaves()
    {
        string settings = Settings();
        new SettingsStore(settings).Save(new()); // Legacy settings do not contain the preference.
        var model = new MainViewModel(settings);
        Assert.False(model.ValidateBeforeConversion);
        model.SaveOptions(model.Options, model.OverwritePolicy, model.Theme, true);
        Assert.True(new MainViewModel(settings).ValidateBeforeConversion);
        model.SaveOptions(model.Options with
        {
            AutoCreateCueText = false
        });
        Assert.True(new MainViewModel(settings).ValidateBeforeConversion);
        model.SaveOptions(model.Options, model.OverwritePolicy, model.Theme, false);
        Assert.False(new MainViewModel(settings).ValidateBeforeConversion);
    }

}
