using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;
namespace KaraokeToolkit.Core.Conversion;

public enum OperationSupport
{
    Supported, Deferred, HardwareUnverified
}
/// <summary>Availability of local conversions, hardware-dependent operations and deferred features.</summary>
public static class OperationCapabilities
{
    public static OperationSupport Describe(Action action) => action switch
    {
        Action.AudioG_To_Bin or Action.AudioG_To_Zip or Action.Zip_To_Bin or Action.Zip_To_AudioG or
        Action.Bin_To_AudioG or Action.Bin_To_Zip or Action.Cleanup_Filename or Action.Cleanup_Filenames or
        Action.Repack_Zip or Action.Archive_To_Zip or Action.LosslessG_To_Mp3G or Action.Karaoke_To_Video or Action.Create_Cue_And_Text_Files => OperationSupport.Supported,
        Action.CDG_To_Bin or Action.CDG_To_AudioG or Action.CDG_To_Zip => OperationSupport.HardwareUnverified,
        Action.Rename_Files_From_Internet or Action.Play_AudioG or Action.Check_For_Internet_Update => OperationSupport.Deferred,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
