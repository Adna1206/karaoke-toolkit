using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Tagging;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.App.Features.Recipes;

/// <summary>Reuses the settings editor while exposing only preferences used by this operation.</summary>
public sealed class RecipeStepOptionsViewModel : SettingsViewModel
{
    public bool HasZipSettings { get; }
    public bool HasAudioSettings { get; }
    public bool HasVideoSettings { get; }
    public bool CanChooseEncoder { get; }
    public bool CanDeleteOriginals { get; }
    public bool HasMetadataSettings { get; }
    public bool HasOverwriteSettings { get; }
    public bool HasOrganizationSettings { get; }
    public bool HasRecursionSettings { get; }
    public bool CanValidateInputs { get; }
    public bool CanAutoCreateMetadata { get; }
    public bool HasTaggingSettings { get; }
    public string TaggingPattern { get; set; } = FilenameTagPattern.Default;
    public bool FillEmptyTagsOnly { get; set; }
    public bool ClearTagComments { get; set; } = true;
    public bool ClearOtherTags { get; set; } = true;

    public RecipeStepOptionsViewModel(RecipeStep step) : base(step.OperationId == "LosslessGToMp3G"
        ? step.Options with { AudioEncoder = AudioEncoder.MP3 } : step.Options)
    {
        var action = RecipeOperations.Get(step.OperationId).Action;
        bool renamesFilenames = action is { } operation && OperationCapabilities.RenamesFilenames(operation);
        HasTaggingSettings = step.OperationId == "TagKaraoke";
        HasZipSettings = HasTaggingSettings || action is Action.AudioGToZip or Action.BinToZip or Action.ArchiveToZip or Action.RepackZip;
        HasAudioSettings = action is Action.BinToAudioG or Action.BinToZip or Action.LosslessGToMp3G;
        HasVideoSettings = action == Action.KaraokeToVideo;
        CanChooseEncoder = action != Action.LosslessGToMp3G;
        CanDeleteOriginals = action != null && !renamesFilenames && action is not (Action.CreateCueAndTextFiles or Action.KaraokeToVideo);
        HasMetadataSettings = action != null && !renamesFilenames && action is not (Action.ArchiveToZip or Action.RepackZip or Action.KaraokeToVideo);
        HasOverwriteSettings = action != null && !renamesFilenames;
        HasOrganizationSettings = step.OperationId == "OrganizeFiles";
        HasRecursionSettings = action != null || step.OperationId == "ValidateKaraoke" || HasTaggingSettings;
        CanValidateInputs = action != null && !renamesFilenames && action is not (Action.CreateCueAndTextFiles or Action.ArchiveToZip or Action.RepackZip);
        CanAutoCreateMetadata = HasMetadataSettings && action != Action.CreateCueAndTextFiles;
        Overwrite = (int)step.OverwritePolicy;
        ValidateBeforeConversion = step.ValidateBeforeConversion;
        IgnoreLeadingTheForFileOrganization = step.IgnoreLeadingThe;
        TaggingPattern = step.TaggingPattern;
        FillEmptyTagsOnly = step.Tagging.FillEmptyOnly;
        ClearTagComments = step.Tagging.ClearComments;
        ClearOtherTags = step.Tagging.ClearOtherDescriptiveTags;
    }

    public RecipeStep ToStep(RecipeStep step) => step with
    {
        Options = ToOptions(), OverwritePolicy = (KaraokeToolkit.Core.Conversion.OverwritePolicy)Overwrite,
        ValidateBeforeConversion = ValidateBeforeConversion, IgnoreLeadingThe = IgnoreLeadingTheForFileOrganization,
        TaggingPattern = TaggingPattern,
        Tagging = new() { FillEmptyOnly = FillEmptyTagsOnly, ClearComments = ClearTagComments, ClearOtherDescriptiveTags = ClearOtherTags }
    };
}
