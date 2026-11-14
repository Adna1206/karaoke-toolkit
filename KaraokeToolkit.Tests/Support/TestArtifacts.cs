using System.Reflection;
using System.Text.RegularExpressions;
namespace KaraokeToolkit.Tests.Support;

/// <summary>Unique test-run fixtures under the repository artifacts directory.</summary>
internal static class TestArtifacts
{
    private static readonly string RunDirectory = Path.Combine(
        typeof(TestArtifacts).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "RepositoryArtifactsPath").Value!,
        "tests", "runs", GetRunId());

    private static string GetRunId()
    {
        string? configured = Environment.GetEnvironmentVariable("KARAOKE_TOOLKIT_TEST_RUN_ID");
        return configured != null && Regex.IsMatch(configured, @"\A[0-9]{8}-[0-9]{6}-[a-f0-9]{32}\z")
            ? configured
            : DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
    }

    // Some workflow tests require a path which does not exist yet.
    internal static string NewPath() => Path.GetFullPath(Path.Combine(RunDirectory, "fixtures", Guid.NewGuid().ToString("N")));
}
