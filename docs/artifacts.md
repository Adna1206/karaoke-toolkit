# Generated artifacts

All custom artifacts live in the repository-root `artifacts/` directory, already
ignored by `.gitignore`. Normal builds still use each project's standard bin/obj.

- `artifacts/builds/<project>/<name>/`: custom build outputs.
- `artifacts/tests/results/`: TRX, coverage and other test-runner output.
- `artifacts/tests/runs/<run-id>/fixtures/`: generated test input/output files.
- `artifacts/publish/<version>/<run-id>/<rid>/`: self-contained publish staging.
- `artifacts/releases/<version>/<run-id>/<rid>/`: Velopack installers, portable
  packages, update metadata and SHA256 checksums. See [publishing](publishing.md).
- `artifacts/runtime/`: reusable extracted native-tool cache.
- `artifacts/benchmarks/`: measurement reports and generated benchmark media.

Builds, publish staging and generated test/benchmark media can be regenerated and
removed when no running process needs them. Keep reports useful for investigations,
packages you want to distribute, and the runtime cache to avoid repeated extraction.
Do not confuse these outputs with source-folder recovery journals or vendored inputs.

Directory.Build.props defines RepositoryArtifactsPath and supports an optional
ArtifactBuildName. Use this instead of relative BaseOutputPath overrides:

```powershell
dotnet build KaraokeToolkit.App/KaraokeToolkit.App.csproj -p:ArtifactBuildName=verification
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj --logger trx
```

KaraokeToolkit.runsettings sets the results location relative to the repository root.
The test project selects it and sets VSTestResultsDirectory. Its assembly metadata
passes the absolute artifacts root to TestArtifacts, which reserves unique fixture
paths per test run. Fixtures are retained for inspection; automatic cleanup is not
implemented. Production recovery folders and temporary file locks are unchanged.

Explicit command-line output/results overrides can still override these defaults.
Do not use a relative `-p:BaseOutputPath=artifacts/...`: MSBuild evaluates that against
each project, recreating the scattered directories this configuration replaces.
