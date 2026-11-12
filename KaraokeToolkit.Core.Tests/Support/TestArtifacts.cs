using System.Reflection;
namespace KaraokeToolkit.Core.Tests.Support;

/// <summary>Unique test-run fixtures under the repository artifacts directory.</summary>
internal static class TestArtifacts
{
    private static readonly string RunDirectory = Path.Combine(
        typeof(TestArtifacts).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "RepositoryArtifactsPath").Value!,
        "tests", "runs", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));

    // Some workflow tests require a path which does not exist yet.
    internal static string NewPath() => Path.GetFullPath(Path.Combine(RunDirectory, "fixtures", Guid.NewGuid().ToString("N")));
}
