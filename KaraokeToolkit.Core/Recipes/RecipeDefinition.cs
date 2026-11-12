using KaraokeToolkit.Core.Conversion;
using System.Text.Json;

namespace KaraokeToolkit.Core.Recipes;

/// <summary>A portable workflow. Input paths belong to a launch, not the saved recipe.</summary>
public sealed record RecipeDefinition
{
    public int FormatVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "New recipe";
    public IReadOnlyList<RecipeStep> Steps { get; init; } = [];
}

/// <summary>Settings are frozen when the step is configured, independently of app preferences.</summary>
public sealed record RecipeStep
{
    public string OperationId { get; init; } = "";
    public ConversionOptions Options { get; init; } = new();
    public OverwritePolicy OverwritePolicy { get; init; } = OverwritePolicy.Ask;
    public bool ValidateBeforeConversion { get; init; }
    public bool IgnoreLeadingThe { get; init; }
}

public static class RecipeJson
{
    private static readonly JsonSerializerOptions Settings = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public static string Serialize(RecipeDefinition recipe)
    {
        RecipeOperations.Validate(recipe);
        return JsonSerializer.Serialize(recipe, Settings);
    }
    public static RecipeDefinition Deserialize(string json)
    {
        var recipe = JsonSerializer.Deserialize<RecipeDefinition>(json, Settings)
            ?? throw new InvalidDataException("The recipe is empty.");
        RecipeOperations.Validate(recipe);
        return recipe;
    }
}
