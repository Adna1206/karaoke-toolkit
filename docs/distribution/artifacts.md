# Generated artifacts

All custom artifacts live in the repository-root `artifacts/` directory, already
ignored by `.gitignore`. Normal builds still use each project's standard bin/obj.

- `artifacts/builds/<project>/<name>/`: custom build outputs.
- `artifacts/tests/results/`: TRX, coverage and other test-runner output.
- `artifacts/tests/runs/<run-id>/fixtures/`: generated test input/output files.
- `artifacts/publish/<version>/<run-id>/<rid>/`: self-contained publish staging.
- `artifacts/releases/<version>/<run-id>/<rid>/`: Velopack installers, portable
  packages, update metadata and SHA256 checksums. See [publishing](publishing.md).
- `artifacts/runtime/`: reusable pinned downloads and extracted native runtimes.
- `artifacts/distribution-sources/`: prepared third-party sources, optional ZIPs
  and checksum sidecars; see [distribution licensing](distribution-licensing.md).
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

Use `./scripts/Test.ps1` for routine serial verification. It reuses the
StandaloneTests build output, writes a TRX report, and deletes successful
run fixtures. Failed fixtures are retained for 14 days by default.
Use `-KeepFixtures` to preserve a run for longer investigation. Raw
`dotnet test` uses isolated fixture paths and leaves them for inspection.

Successful publish staging is removed after packages and checksums are
verified. Failed staging is marked for retention. The test wrapper also runs
`./scripts/Clean-Artifacts.ps1` to remove marked runs older than 14 days.
Run that command manually with `-WhatIf` to preview cleanup, or with
`-RetentionDays` to choose the cutoff. Unmarked runs, release packages,
test reports, runtime caches and source evidence are outside its scope.

Explicit command-line output/results overrides can still override these defaults.
Do not use a relative `-p:BaseOutputPath=artifacts/...`: MSBuild evaluates that against
each project, recreating the scattered directories this configuration replaces.
