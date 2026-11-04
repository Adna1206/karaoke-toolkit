using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Tagging;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Core.Recipes;

[Flags]
public enum RecipeInputKind { None = 0, Folders = 1, AudioGraphics = 2, Zip = 4, Bin = 8, Archive = 16, Video = 32 }

/// <summary>Known chaining constraints; file contents and runtime support are checked by the engines.</summary>
public sealed record RecipeOperation(OperationId Operation, RecipeInputKind Inputs, RecipeInputKind Outputs, Action? Action = null)
{
    // Recipe JSON uses the same named identities as other operation requests.
    public string Id => OperationCatalog.Get(Operation).Identifier;
}

public static class RecipeOperations
{
    private const RecipeInputKind Karaoke = RecipeInputKind.AudioGraphics | RecipeInputKind.Zip | RecipeInputKind.Bin;
    public static IReadOnlyList<RecipeOperation> All { get; } = Array.AsReadOnly<RecipeOperation>([
        new(OperationId.ZipToBin, RecipeInputKind.Folders | RecipeInputKind.Zip, RecipeInputKind.Bin, Action.ZipToBin),
        new(OperationId.AudioGToBin, RecipeInputKind.Folders | RecipeInputKind.AudioGraphics, RecipeInputKind.Bin, Action.AudioGToBin),
        new(OperationId.BinToZip, RecipeInputKind.Folders | RecipeInputKind.Bin, RecipeInputKind.Zip, Action.BinToZip),
        new(OperationId.AudioGToZip, RecipeInputKind.Folders | RecipeInputKind.AudioGraphics, RecipeInputKind.Zip, Action.AudioGToZip),
        new(OperationId.ZipToAudioG, RecipeInputKind.Folders | RecipeInputKind.Zip, RecipeInputKind.AudioGraphics, Action.ZipToAudioG),
        new(OperationId.BinToAudioG, RecipeInputKind.Folders | RecipeInputKind.Bin, RecipeInputKind.AudioGraphics, Action.BinToAudioG),
        new(OperationId.RepackZip, RecipeInputKind.Folders | RecipeInputKind.Zip, RecipeInputKind.Zip, Action.RepackZip),
        new(OperationId.ArchiveToZip, RecipeInputKind.Folders | RecipeInputKind.Archive, RecipeInputKind.Zip, Action.ArchiveToZip),
        new(OperationId.LosslessGToMp3G, RecipeInputKind.Folders | RecipeInputKind.AudioGraphics, RecipeInputKind.AudioGraphics, Action.LosslessGToMp3G),
        new(OperationId.KaraokeToVideo, RecipeInputKind.Folders | Karaoke, RecipeInputKind.Video, Action.KaraokeToVideo),
        new(OperationId.CreateCueAndTextFiles, RecipeInputKind.Folders, RecipeInputKind.None, Action.CreateCueAndTextFiles),
        new(OperationId.CleanUpFilenames, RecipeInputKind.Folders, RecipeInputKind.None, Action.CleanUpFilenames),
        new(OperationId.OrganizeFiles, RecipeInputKind.Folders | Karaoke | RecipeInputKind.Archive | RecipeInputKind.Video, RecipeInputKind.None),
        new(OperationId.OrganizeFolders, RecipeInputKind.Folders, RecipeInputKind.None),
        new(OperationId.FlattenOneLayer, RecipeInputKind.Folders, RecipeInputKind.None),
        new(OperationId.FlattenAllLayers, RecipeInputKind.Folders, RecipeInputKind.None),
        new(OperationId.ValidateKaraoke, RecipeInputKind.Folders | Karaoke, RecipeInputKind.None),
        new(OperationId.TagKaraoke, RecipeInputKind.Folders | RecipeInputKind.AudioGraphics | RecipeInputKind.Zip | RecipeInputKind.Video, RecipeInputKind.None),
    ]);

    public static bool Supports(string id) => All.Any(operation => operation.Id == id);

    public static RecipeOperation Get(string id) => All.FirstOrDefault(operation => operation.Id == id)
        ?? throw new NotSupportedException("This operation cannot run in a recipe: " + id);

    /// <summary>Null means arbitrary files; empty means folders only. Used by pickers and launch checks.</summary>
    public static IReadOnlyList<string>? FileExtensions(string operationId)
    {
        var operation = Get(operationId);
        if (operationId == "OrganizeFiles") return null;
        if (operationId == "TagKaraoke") return KaraokeTaggingService.MediaExtensions.Concat([".zip", ".cdg"]).ToArray();
        if (operationId == "LosslessGToMp3G") return LosslessAudioTranscoder.Extensions.Append(".cdg").ToArray();
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
            if (operation.Id == "TagKaraoke")
            {
                _ = new FilenameTagPattern(step.TaggingPattern);
                if (step.Tagging == null) throw new ArgumentException("Tagging settings are missing.");
            }
            new ConversionRequest(operation.Action ?? Action.AudioGToZip, ".", step.Options).Validate();
            // None means pass-through: validation, listings and folder maintenance retain their input scope.
            if (operation.Outputs != RecipeInputKind.None) previous = operation.Outputs;
            else if (operation.Inputs == RecipeInputKind.Folders) previous = RecipeInputKind.Folders;
        }
    }
}
