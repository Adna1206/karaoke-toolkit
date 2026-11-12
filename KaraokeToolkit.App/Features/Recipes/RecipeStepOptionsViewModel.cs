using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Recipes;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.App.Features.Recipes;

/// <summary>Reuses the settings editor while exposing only preferences used by this operation.</summary>
public sealed class RecipeStepOptionsViewModel : OptionsViewModel
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

    public RecipeStepOptionsViewModel(RecipeStep step) : base(step.OperationId == "LosslessG_To_Mp3G"
        ? step.Options with { AudioEncoder = AudioEncoder.MP3 } : step.Options)
    {
        var action = RecipeOperations.Get(step.OperationId).Action;
        HasZipSettings = action is Action.AudioG_To_Zip or Action.Bin_To_Zip or Action.Archive_To_Zip or Action.Repack_Zip;
        HasAudioSettings = action is Action.Bin_To_AudioG or Action.Bin_To_Zip or Action.LosslessG_To_Mp3G;
        HasVideoSettings = action == Action.Karaoke_To_Video;
        CanChooseEncoder = action != Action.LosslessG_To_Mp3G;
        CanDeleteOriginals = action != null && action is not (Action.Create_Cue_And_Text_Files or Action.Cleanup_Filenames or Action.Karaoke_To_Video);
        HasMetadataSettings = action != null && action is not (Action.Cleanup_Filenames or Action.Archive_To_Zip or Action.Repack_Zip or Action.Karaoke_To_Video);
        HasOverwriteSettings = action != null && action != Action.Cleanup_Filenames;
        HasOrganizationSettings = step.OperationId == "OrganizeFiles";
        HasRecursionSettings = action != null || step.OperationId == "ValidateKaraoke";
        CanValidateInputs = action != null && action is not (Action.Cleanup_Filenames or Action.Create_Cue_And_Text_Files or Action.Archive_To_Zip or Action.Repack_Zip);
        CanAutoCreateMetadata = HasMetadataSettings && action != Action.Create_Cue_And_Text_Files;
        Overwrite = (int)step.OverwritePolicy;
        ValidateBeforeConversion = step.ValidateBeforeConversion;
        IgnoreLeadingTheForFileOrganization = step.IgnoreLeadingThe;
    }
}
