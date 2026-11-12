using System.Collections.Generic;
namespace KaraokeToolkit.App.Metadata;

public enum SettingCategory { General, Legacy, Archives, Audio, DiscRipping, Video }

/// <summary>Presentation metadata only. Defaults, saved values and validation remain in their typed models.</summary>
public record SettingDefinition(string Id, string Label, string ShortDescription, string Description, SettingCategory Category);
public sealed record ChoiceSettingDefinition<T>(string Id, string Label, string ShortDescription, string Description,
    SettingCategory Category, IReadOnlyList<T> Choices) : SettingDefinition(Id, Label, ShortDescription, Description, Category);
public sealed record HelpDefinition(string Label, string Description);
