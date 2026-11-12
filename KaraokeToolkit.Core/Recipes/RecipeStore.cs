namespace KaraokeToolkit.Core.Recipes;

/// <summary>Local recipe library. GUID filenames keep user names out of filesystem paths.</summary>
public sealed class RecipeStore(string directory)
{
    public IReadOnlyList<RecipeDefinition> Load() => Directory.Exists(directory)
        ? Directory.GetFiles(directory, "*.json").Select(path => RecipeJson.Deserialize(File.ReadAllText(path))).OrderBy(recipe => recipe.Name).ToArray()
        : [];

    public void Save(RecipeDefinition recipe)
    {
        string json = RecipeJson.Serialize(recipe);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, recipe.Id.ToString("D") + ".json");
        string temporary = path + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Removes only the library JSON for this ID; input/output files are never touched.</summary>
    public void Delete(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A saved recipe ID is required.", nameof(id));
        File.Delete(Path.Combine(directory, id.ToString("D") + ".json"));
    }
}
