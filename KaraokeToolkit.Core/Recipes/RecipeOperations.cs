using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Recovery;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Core.Recipes;

[Flags]
public enum RecipeInputKind { None = 0, Folders = 1, AudioGraphics = 2, Zip = 4, Bin = 8, Archive = 16, Video = 32 }

/// <summary>Known chaining constraints; file contents and runtime support are checked by the engines.</summary>
public sealed record RecipeOperation(string Id, RecipeInputKind Inputs, RecipeInputKind Outputs, Action? Action = null);

public static class RecipeOperations
{
    private const RecipeInputKind Karaoke = RecipeInputKind.AudioGraphics | RecipeInputKind.Zip | RecipeInputKind.Bin;
    public static IReadOnlyList<RecipeOperation> All { get; } = Array.AsReadOnly<RecipeOperation>([
        new("Zip_To_Bin", RecipeInputKind.Folders | RecipeInputKind.Zip, RecipeInputKind.Bin, Action.Zip_To_Bin),
        new("AudioG_To_Bin", RecipeInputKind.Folders | RecipeInputKind.AudioGraphics, RecipeInputKind.Bin, Action.AudioG_To_Bin),
        new("Bin_To_Zip", RecipeInputKind.Folders | RecipeInputKind.Bin, RecipeInputKind.Zip, Action.Bin_To_Zip),
        new("AudioG_To_Zip", RecipeInputKind.Folders | RecipeInputKind.AudioGraphics, RecipeInputKind.Zip, Action.AudioG_To_Zip),
        new("Zip_To_AudioG", RecipeInputKind.Folders | RecipeInputKind.Zip, RecipeInputKind.AudioGraphics, Action.Zip_To_AudioG),
        new("Bin_To_AudioG", RecipeInputKind.Folders | RecipeInputKind.Bin, RecipeInputKind.AudioGraphics, Action.Bin_To_AudioG),
        new("Repack_Zip", RecipeInputKind.Folders | RecipeInputKind.Zip, RecipeInputKind.Zip, Action.Repack_Zip),
        new("Archive_To_Zip", RecipeInputKind.Folders | RecipeInputKind.Archive, RecipeInputKind.Zip, Action.Archive_To_Zip),
        new("LosslessG_To_Mp3G", RecipeInputKind.Folders | RecipeInputKind.AudioGraphics, RecipeInputKind.AudioGraphics, Action.LosslessG_To_Mp3G),
        new("Karaoke_To_Video", RecipeInputKind.Folders | Karaoke, RecipeInputKind.Video, Action.Karaoke_To_Video),
        new("Create_Cue_And_Text_Files", RecipeInputKind.Folders, RecipeInputKind.None, Action.Create_Cue_And_Text_Files),
        new("Cleanup_Filenames", RecipeInputKind.Folders, RecipeInputKind.None, Action.Cleanup_Filenames),
        new("OrganizeFiles", RecipeInputKind.Folders | Karaoke | RecipeInputKind.Archive | RecipeInputKind.Video, RecipeInputKind.None),
        new("OrganizeFolders", RecipeInputKind.Folders, RecipeInputKind.None),
        new("FlattenOneLayer", RecipeInputKind.Folders, RecipeInputKind.None),
        new("FlattenAllLayers", RecipeInputKind.Folders, RecipeInputKind.None),
        new("ValidateKaraoke", RecipeInputKind.Folders | Karaoke, RecipeInputKind.None),
    ]);

    public static RecipeOperation Get(string id) => All.FirstOrDefault(operation => operation.Id == id)
        ?? throw new NotSupportedException("This operation cannot run in a recipe: " + id);

    /// <summary>Null means arbitrary files; empty means folders only. Used by pickers and launch checks.</summary>
    public static IReadOnlyList<string>? FileExtensions(string operationId)
    {
        var operation = Get(operationId);
        if (operationId == "OrganizeFiles") return null;
        if (operationId == "LosslessG_To_Mp3G") return LosslessAudioTranscoder.Extensions.Append(".cdg").ToArray();
        var extensions = new List<string>();
        if (operation.Inputs.HasFlag(RecipeInputKind.Zip)) extensions.Add(".zip");
        if (operation.Inputs.HasFlag(RecipeInputKind.Bin)) extensions.Add(".bin");
        if (operation.Inputs.HasFlag(RecipeInputKind.Archive)) extensions.AddRange(ArchiveZipConverter.Extensions);
        if (operation.Inputs.HasFlag(RecipeInputKind.AudioGraphics)) extensions.AddRange(KaraokeArchive.AudioExtensions.Append(".cdg"));
        return extensions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Checks selected paths against the first step; detailed media validation happens at execution.</summary>
    public static string[] NormalizeInputs(RecipeDefinition recipe, IEnumerable<string> paths)
    {
        Validate(recipe);
        var extensions = FileExtensions(recipe.Steps[0].OperationId);
        var selected = paths.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).Distinct(InputSelection.Paths).ToArray();
        if (selected.Length == 0) throw new ArgumentException("Select input files or folders for the recipe.");
        foreach (string path in selected)
        {
            RunSession.CheckPlainPath(path);
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Recipe input is missing.", path);
            if (path.Split(Path.DirectorySeparatorChar).Any(InputSelection.IsExcludedFolder))
                throw new IOException("Recovery and Damaged folders cannot be recipe inputs.");
            if (File.Exists(path) && extensions != null && !extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException(extensions.Count == 0 ? "This recipe starts with an operation requiring folder selections."
                    : "This file type is not supported by the recipe's first step: " + path);
        }
        return selected;
    }

    public static bool CanAcceptInputs(RecipeDefinition recipe, IEnumerable<string> paths)
    {
        try { NormalizeInputs(recipe, paths); return true; }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }

    public static void Validate(RecipeDefinition recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (recipe.FormatVersion != 1) throw new NotSupportedException("Unsupported recipe format version.");
        if (recipe.Id == Guid.Empty || string.IsNullOrWhiteSpace(recipe.Name) || recipe.Name.Length > 200)
            throw new ArgumentException("Give the recipe a name of at most 200 characters and a valid ID.");
        if (recipe.Steps is not { Count: > 0 and <= 100 }) throw new ArgumentException("A recipe needs between 1 and 100 steps.");
        RecipeInputKind previous = RecipeInputKind.None;
        for (int index = 0; index < recipe.Steps.Count; index++)
        {
            var step = recipe.Steps[index] ?? throw new ArgumentException("A recipe step is missing.");
            var operation = Get(step.OperationId);
            if (previous != RecipeInputKind.None && (previous & operation.Inputs) == 0)
                throw new ArgumentException($"Step {index + 1} ({operation.Id}) cannot accept the previous step's output.");
            if (!Enum.IsDefined(step.OverwritePolicy)) throw new ArgumentException("Invalid overwrite policy.");
            new OperationRequest(operation.Action ?? Action.AudioG_To_Zip, ".", step.Options).Validate();
            // None means pass-through: validation, listings and folder maintenance retain their input scope.
            if (operation.Outputs != RecipeInputKind.None) previous = operation.Outputs;
            else if (operation.Inputs == RecipeInputKind.Folders) previous = RecipeInputKind.Folders;
        }
    }
}
