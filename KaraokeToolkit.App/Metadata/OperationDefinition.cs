using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.App.Metadata;

public enum OperationSection { KaraokeConversion, Archives, FilesAndFolders, ValidationAndReports, DiscRipping, Favourites, Recipes }
public enum OperationInputKind { None, FilesAndFolders, Folders }

/// <summary>Immutable UI metadata and routing for one operation. Core remains responsible for format validation.</summary>
public sealed record OperationDefinition(string Id, Action? Action, string Label, string ShortDescription,
    string Description, OperationSection Section, OperationInputKind Inputs, bool Available)
{
    public bool IsRip { get; init; }
    public bool IsRestore { get; init; }
    public bool IsTagging { get; init; }
    public AlphabeticalOrganizeScope? OrganizeMode { get; init; }
    public FolderFlattenScope? FlattenScope { get; init; }
    public bool IsValidation => Section != OperationSection.Recipes && Action == null && !IsRip && !IsRestore && !IsTagging && OrganizeMode == null && FlattenScope == null;
}

public sealed record OperationSectionDefinition(OperationSection Section, string Header);
