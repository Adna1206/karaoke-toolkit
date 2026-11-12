using System;
using System.Collections.Generic;
using KaraokeToolkit.Core.FileOperations;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.App.Metadata;

/// <summary>Presentation order and definitions, shared by the operation panel and favourites.</summary>
public static class OperationDefinitions
{
    public static IReadOnlyList<OperationDefinition> All { get; } = Array.AsReadOnly<OperationDefinition>([
        new(
            "Zip_To_Bin", Action.Zip_To_Bin, "Convert ZIP to BIN",
            "Convert karaoke ZIP archives to BIN files beside the originals.",
            "Convert karaoke ZIP archives to BIN files beside the originals.",
            OperationSection.KaraokeConversion, OperationInputKind.FilesAndFolders, true),
        new(
            "AudioG_To_Bin", Action.AudioG_To_Bin, "Convert Audio+G to BIN",
            "Combine matching audio and CDG graphics into BIN files.",
            "Combine matching audio and CDG graphics into BIN files.",
            OperationSection.KaraokeConversion, OperationInputKind.FilesAndFolders, true),
        new(
            "Bin_To_Zip", Action.Bin_To_Zip, "Convert BIN to ZIP",
            "Convert BIN tracks into compressed audio and CDG pairs in ZIP archives.",
            "Convert BIN tracks into compressed audio and CDG pairs in ZIP archives.",
            OperationSection.KaraokeConversion, OperationInputKind.FilesAndFolders, true),
        new(
            "AudioG_To_Zip", Action.AudioG_To_Zip, "Convert Audio+G to ZIP",
            "Package matching audio and CDG graphics into ZIP archives.",
            "Package matching audio and CDG graphics into ZIP archives.",
            OperationSection.KaraokeConversion, OperationInputKind.FilesAndFolders, true),
        new(
            "Zip_To_AudioG", Action.Zip_To_AudioG, "Convert ZIP to Audio+G",
            "Extract audio and CDG graphics beside each ZIP.",
            "Extract audio and CDG graphics beside each ZIP. Existing files follow the overwrite policy in Options.",
            OperationSection.KaraokeConversion, OperationInputKind.FilesAndFolders, true),
        new(
            "Bin_To_AudioG", Action.Bin_To_AudioG, "Convert BIN to Audio+G",
            "Extract CDG graphics and encode the audio from BIN tracks.",
            "Extract CDG graphics and encode the audio from BIN tracks.",
            OperationSection.KaraokeConversion, OperationInputKind.FilesAndFolders, true),
        new(
            "Repack_Zip", Action.Repack_Zip, "Repack ZIP for Compatibility",
            "Rebuild ZIP archives using your ZIP algorithm and compression level in Options.",
            "Rebuild ZIP archives using your ZIP algorithm and compression level in Options. Deflate (the default) is recommended for fussy legacy karaoke players. Preserves all files and folders; replaces the source ZIP when Delete Originals is enabled, otherwise saves a separate '(repacked).zip'. Encrypted and split archives are unsupported.",
            OperationSection.Archives, OperationInputKind.FilesAndFolders, true),
        new(
            "Archive_To_Zip", Action.Archive_To_Zip, "Convert Archive to ZIP",
            "Convert supported non-ZIP archives to ZIP using your compression settings.",
            "Convert supported non-ZIP archives to ZIP using your compression settings. Preserves all files and folders. Encrypted and split archives are unsupported.",
            OperationSection.Archives, OperationInputKind.FilesAndFolders, true),
        new(
            "LosslessG_To_Mp3G", Action.LosslessG_To_Mp3G, "Convert Lossless Audio+G to MP3+G",
            "Encode matching WAV, FLAC, APE, AIFF, ALAC or WavPack audio to MP3 while keeping the CDG unchanged.",
            "Encode matching WAV, FLAC, APE, AIFF, ALAC or WavPack audio to MP3 while keeping the CDG unchanged. Accepts stereo lossless audio at any valid sample rate and outputs 44.1 kHz MP3 using the bitrate in Options.",
            OperationSection.KaraokeConversion, OperationInputKind.FilesAndFolders, true),
        new(
            "Karaoke_To_Video", Action.Karaoke_To_Video, "Convert Karaoke to Video",
            "Render BIN, ZIP or matching Audio+G as video with synchronized CDG graphics.",
            "Render BIN, ZIP or matching Audio+G as video with synchronized CDG graphics. Choose MP4, MKV or AVI and encoding settings in Options. Source files are kept.",
            OperationSection.KaraokeConversion, OperationInputKind.FilesAndFolders, true),
        new(
            "Rename_Files_From_Internet", Action.Rename_Files_From_Internet, "Rename Files from Internet",
            "Deferred: the original database has closed.",
            "Deferred: the original database has closed. A replacement is future work.",
            OperationSection.KaraokeConversion, OperationInputKind.FilesAndFolders, false),
        new(
            "Create_Cue_And_Text_Files", Action.Create_Cue_And_Text_Files, "Create CUE/TXT Files",
            "Create CUE sheets for BIN tracks and text track listings.",
            "Create CUE sheets for BIN tracks and text track listings.",
            OperationSection.ValidationAndReports, OperationInputKind.FilesAndFolders, true),
        new(
            "Cleanup_Filenames", Action.Cleanup_Filenames, "Clean Up Filenames",
            "Normalize local karaoke filenames, preserving matching audio/CDG pairs.",
            "Normalize local karaoke filenames, preserving matching audio/CDG pairs. Collisions leave files unchanged.",
            OperationSection.FilesAndFolders, OperationInputKind.FilesAndFolders, true),
        new(
            "OrganizeFiles", null, "Organize Files Alphabetically",
            "Move files in selected folders and their subfolders into local A–Z and # folders.",
            "Move files in selected folders and their subfolders into local A–Z and # folders. Selected files are moved without processing their neighbours. Options can ignore a leading 'The' when choosing a folder. Existing letter folders and conflicting names are left in place.",
            OperationSection.FilesAndFolders, OperationInputKind.FilesAndFolders, true) { OrganizeMode = AlphabeticalOrganizeScope.Files },
        new(
            "OrganizeFolders", null, "Organize Folders Alphabetically",
            "Move only the immediate child folders of each selected folder into A–Z and # folders.",
            "Move only the immediate child folders of each selected folder into A–Z and # folders. Does not recurse or move files. Existing letter folders and conflicting names are left in place.",
            OperationSection.FilesAndFolders, OperationInputKind.Folders, true) { OrganizeMode = AlphabeticalOrganizeScope.Folders },
        new(
            "FlattenOneLayer", null, "Flatten Folder Layer",
            "Move the contents of each immediate child folder into the selected folder, then remove empty child folders.",
            "Move the contents of each immediate child folder into the selected folder, then remove empty child folders. Deeper folders move intact. Conflicting names remain in their original child folder.",
            OperationSection.FilesAndFolders, OperationInputKind.Folders, true) { FlattenScope = FolderFlattenScope.OneLayer },
        new(
            "FlattenAllLayers", null, "Flatten All Folder Layers",
            "Move files from every descendant folder into each selected root folder, then remove empty folders from the bottom up.",
            "Move files from every descendant folder into each selected root folder, then remove empty folders from the bottom up. Conflicting names remain in their original folders. Protected and linked folders are left in place.",
            OperationSection.FilesAndFolders, OperationInputKind.Folders, true) { FlattenScope = FolderFlattenScope.AllLayers },
        new(
            "DiscRip", null, "Rip CD+G (Experimental)",
            "Read selected disc tracks to BIN, Audio+G or ZIP.",
            "Read selected disc tracks to BIN, Audio+G or ZIP. Uses cdrdao; physical drive reading is unverified. Click Start to choose a drive and destination.",
            OperationSection.DiscRipping, OperationInputKind.None, true) { IsRip = true },
        new(
            "RestoreQuarantined", null, "Restore Quarantined Files",
            "Restore files previously moved into a Damaged folder to their original locations.",
            "Restore files previously moved into a Damaged folder to their original locations. Click Start to choose the batch manifest.json; existing files are never replaced. No source folder is needed.",
            OperationSection.ValidationAndReports, OperationInputKind.None, true) { IsRestore = true },
        new(
            "ValidateKaraoke", null, "Validate Karaoke Files",
            "Check audio/CDG pairs, BIN and CDG structure, and ZIP archive integrity.",
            "Check audio/CDG pairs, CDG packet structure, BIN sector structure, and ZIP integrity. Identify missing, ambiguous, damaged, or unsupported files without converting or moving them. Audio decoding, graphics rendering, and synchronisation checks are not included.",
            OperationSection.ValidationAndReports, OperationInputKind.FilesAndFolders, true),
    ]);
    public static IReadOnlyList<OperationSectionDefinition> Sections { get; } = Array.AsReadOnly<OperationSectionDefinition>([
        new(OperationSection.Favourites, "Favourites"),
        new(OperationSection.KaraokeConversion, "Karaoke Conversion"),
        new(OperationSection.Archives, "Archive Tools"),
        new(OperationSection.FilesAndFolders, "File & Folder Tools"),
        new(OperationSection.ValidationAndReports, "Validation & Reports"),
        new(OperationSection.Recipes, "Recipes (Experimental)"),
        new(OperationSection.DiscRipping, "Disc Ripping")
    ]);
}
