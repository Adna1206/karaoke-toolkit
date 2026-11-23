using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.App.Metadata;

public enum OperationSection { KaraokeConversion, Archives, FileTools, FolderTools, ValidationAndReports, DiscRipping, Favourites, Recipes }
public enum OperationInputKind { None, FilesAndFolders, Folders, SingleManifest, SingleDisc }

/// <summary>Immutable UI metadata and routing for one operation. Core remains responsible for format validation.</summary>
public sealed record OperationDefinition(string Id, Action? Action, string Label, string ShortDescription,
    string Description, OperationSection Section, OperationInputKind Inputs, bool Available)
{
    public OperationDefinition(OperationId operation, Action? action, string label, string shortDescription,
        string description, OperationSection section, OperationInputKind inputs, bool available)
        : this(OperationCatalog.Get(operation).Identifier, action, label, shortDescription, description, section, inputs, available) { }

    // Saved favourite IDs remain strings; dynamic saved recipes use Recipe:<GUID>.
    public OperationId? Operation => Section == OperationSection.Recipes ? null : OperationCatalog.Get(Id).Id;
    public bool AllowsMultipleSources => Inputs is OperationInputKind.FilesAndFolders or OperationInputKind.Folders;
    public bool IsRip => Inputs == OperationInputKind.SingleDisc;
    public bool IsRestore => Inputs == OperationInputKind.SingleManifest;
    public bool IsTagging { get; init; }
    public bool IsTrackGrouping => Operation == OperationId.GroupByTrackId;
    public bool IsAudioGain => Operation == OperationId.NormalizeAudioGain;
    public bool IsAudioKey => Operation == OperationId.ChangeAudioKey;
    public bool IsFilenameRename => Operation == OperationId.RenameByPattern;
    // The Core allowlist is authoritative for both the builder and imported recipes.
    public bool CanUseInRecipes => KaraokeToolkit.Core.Recipes.RecipeOperations.Supports(Id);
    public AlphabeticalOrganizeScope? OrganizeMode { get; init; }
    public FolderFlattenScope? FlattenScope { get; init; }
    public bool IsValidation => Section != OperationSection.Recipes && Action == null && !IsRip && !IsRestore && !IsTagging &&
        !IsTrackGrouping && !IsAudioGain && !IsAudioKey && !IsFilenameRename && OrganizeMode == null && FlattenScope == null;
}

public sealed record OperationSectionDefinition(OperationSection Section, string Header);
