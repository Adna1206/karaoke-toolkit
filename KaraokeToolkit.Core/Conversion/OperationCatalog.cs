// Original operation identifiers retain their reference values; new operations are appended.
// This catalog does not execute or modify the original conversion routines.
#nullable disable
namespace KaraokeToolkit.Core.Conversion;

public sealed class OperationCatalog
{
	public enum defAction
	{
		Zip_To_Bin,
		Zip_To_AudioG,
		Bin_To_Zip,
		Bin_To_AudioG,
		AudioG_To_Bin,
		AudioG_To_Zip,
		CDG_To_Bin,
		CDG_To_Zip,
		CDG_To_AudioG,
		Cleanup_Filename,
		Cleanup_Filenames,
		Rename_Files_From_Internet,
		Create_Cue_And_Text_Files,
		Play_AudioG,
		Check_For_Internet_Update,
        Archive_To_Zip,
        LosslessG_To_Mp3G,
        Karaoke_To_Video,
        Repack_Zip
	}
	private string GetActionText(defAction action)
	{
		string GetActionText = default;
		return action switch
		{
			defAction.Repack_Zip => "Repack ZIP for Compatibility",
            defAction.Archive_To_Zip => "Convert Archive to ZIP",
            defAction.LosslessG_To_Mp3G => "Convert Lossless Audio+G to MP3+G",
            defAction.Karaoke_To_Video => "Convert Karaoke to Video",
            defAction.AudioG_To_Bin => "Convert Audio+G To Bin",
			defAction.AudioG_To_Zip => "Convert Audio+G To Zip", 
			defAction.Bin_To_AudioG => "Convert Bin To Audio+G", 
			defAction.Bin_To_Zip => "Convert Bin To Zip", 
			defAction.CDG_To_AudioG => "Convert CD+G To Audio+G", 
			defAction.CDG_To_Bin => "Convert CD+G To Bin", 
			defAction.CDG_To_Zip => "Convert CD+G To Zip", 
			defAction.Cleanup_Filename => "Cleanup Filename", 
			defAction.Cleanup_Filenames => "Cleanup Filenames", 
			defAction.Create_Cue_And_Text_Files => "Create Cue and Text Files", 
			defAction.Play_AudioG => "Play Audio+G or CD+G", 
			defAction.Rename_Files_From_Internet => "Rename Files From the Internet", 
			defAction.Zip_To_AudioG => "Convert Zip To Audio+G", 
			defAction.Zip_To_Bin => "Convert Zip To Bin", 
			_ => GetActionText, 
		};
	}
    public IReadOnlyList<OperationDescription> Operations { get; }

    public OperationCatalog()
    {
        Operations = Enum.GetValues<defAction>()
            .Select(action => new OperationDescription((int)action, action.ToString(), GetActionText(action)))
            .ToArray();
    }
}

public sealed record OperationDescription(int Id, string Identifier, string OriginalLabel)
{
    public string DisplayLabel => OriginalLabel ?? Identifier;
}
