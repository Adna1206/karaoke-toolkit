using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Tests.App.Metadata;

public class DefinitionTests
{
    [Fact]
    public void CataloguesHaveUniqueIdsAndCompleteHelp()
    {
        Assert.Equal(OperationDefinitions.All.Count, OperationDefinitions.All.Select(definition => definition.Id).Distinct().Count());
        Assert.All(OperationDefinitions.All, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Label));
            Assert.False(string.IsNullOrWhiteSpace(definition.ShortDescription));
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
            if (definition.IsRip)
                Assert.DoesNotContain(OperationDefinitions.Sections, section => section.Section == definition.Section);
            else
                Assert.Contains(OperationDefinitions.Sections, section => section.Section == definition.Section);
            if (definition.Action.HasValue) Assert.Equal(definition.Action.ToString(), definition.Id);
        });
        var settings = SettingsDefinitions.Instance.All;
        Assert.Equal(settings.Count, settings.Select(definition => definition.Id).Distinct().Count());
        Assert.All(settings, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Label));
            Assert.False(string.IsNullOrWhiteSpace(definition.ShortDescription));
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
        });
    }

    [Fact]
    public void SharedDefinitionsDoNotShareUserFavouriteState()
    {
        var first = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        var second = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        first.Operations[0].ToggleFavouriteCommand.Execute(null);
        Assert.Same(first.Operations[0].Definition, second.Operations[0].Definition);
        Assert.True(first.Operations[0].IsFavourite);
        Assert.False(second.Operations[0].IsFavourite);
    }

    [Fact]
    public void OperationInputMetadataControlsSourceSelection()
    {
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        foreach (var operation in model.Operations)
        {
            model.SelectedOperation = operation;
            Assert.Equal(operation.Definition.Inputs is OperationInputKind.FilesAndFolders or OperationInputKind.Folders, model.CanChooseSource);
            Assert.Equal(operation.Definition.Inputs == OperationInputKind.FilesAndFolders, model.CanChooseFiles);
            Assert.Equal(operation.Definition.Operation is { } id && OperationCatalog.Get(id).RequiresFolders,
                operation.Definition.Inputs == OperationInputKind.Folders);
        }
        Assert.Equal(OperationInputKind.SingleDisc, model.Operations.Single(operation => operation.IsRip).Definition.Inputs);
        Assert.Equal(OperationInputKind.SingleManifest, model.Operations.Single(operation => operation.IsRestore).Definition.Inputs);
    }

    [Fact]
    public void DisplayedChoicesIncludeTypedDefaultsAndPassCoreValidation()
    {
        var definitions = SettingsDefinitions.Instance;
        var defaults = new ConversionOptions();
        Assert.Contains(defaults.AudioBitrate, definitions.Bitrate.Choices);
        Assert.Contains(defaults.VideoFrameRate, definitions.VideoFrameRate.Choices);
        Assert.Contains(defaults.VideoBitrateKbps, definitions.VideoBitrateKbps.Choices);
        Assert.Contains(defaults.VideoAudioBitrateKbps, definitions.VideoAudioBitrateKbps.Choices);
        Assert.Equal(Enum.GetValues<AudioEncoder>().Length, definitions.Encoder.Choices.Count);
        Assert.Equal(Enum.GetValues<ZipAlgorithm>().Length, definitions.Algorithm.Choices.Count);
        Assert.Equal(Enum.GetValues<ApplicationTheme>().Length, definitions.Theme.Choices.Count);
        Assert.Equal(Enum.GetValues<VideoResolution>().Length, definitions.VideoResolution.Choices.Count);
        Assert.Equal(Enum.GetValues<VideoGraphicsSmoothing>().Length, definitions.VideoGraphicsSmoothing.Choices.Count);
        Assert.Equal(Enum.GetValues<OverwritePolicy>().Length, definitions.Overwrite.Choices.Count);
        foreach (int bitrate in definitions.Bitrate.Choices)
            foreach (var encoder in Enum.GetValues<AudioEncoder>()) AudioEncoderService.Validate(encoder, bitrate);
        foreach (int rate in definitions.VideoFrameRate.Choices) VideoOptions.Validate(defaults with { VideoFrameRate = rate });
        foreach (var resolution in Enum.GetValues<VideoResolution>()) VideoOptions.Validate(defaults with { VideoResolution = resolution });
        foreach (var smoothing in Enum.GetValues<VideoGraphicsSmoothing>()) VideoOptions.Validate(defaults with { VideoGraphicsSmoothing = smoothing });
        foreach (int rate in definitions.VideoBitrateKbps.Choices) VideoOptions.Validate(defaults with { VideoBitrateKbps = rate });
        foreach (int rate in definitions.VideoAudioBitrateKbps.Choices) VideoOptions.Validate(defaults with { VideoAudioBitrateKbps = rate });
        foreach (int level in Enumerable.Range(0, definitions.CompressionLevel.Choices.Count))
            new ConversionRequest(OperationId.AudioGToZip, ".", defaults with { ZipCompression = (ZipCompression)level }).Validate();
    }
}
