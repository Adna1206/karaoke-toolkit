# Current handoff

## 2026.11.17 Windows x64 prerelease

Released 2026-11-16 from `master` commit `4fa7a4e`, tagged `v2026.11.17`.
The [public GitHub release](https://github.com/Adna1206/karaoke-toolkit/releases/tag/v2026.11.17)
contains the unsigned Windows Setup EXE, Velopack feed and full package,
third-party source ZIP, and checksums. GitHub supplies the Toolkit source
archives from the tag. See [publishing](distribution/publishing.md) for the
commands and asset list.

- `scripts/Test.ps1 -NoRestore` passed 742/742 on Windows.
- `scripts/Publish.ps1 -RuntimeIdentifier win-x64` built the Windows package
  from the tag. The packaged CLI tagged a WebM fixture and preserved both
  encoded stream hashes.
- `scripts/Prepare-DistributionSources.ps1 -Archive` generated and verified
  the Windows third-party source ZIP. GitHub verified all five uploads, and
  the public Windows feed matched the full package on an anonymous check.

Next, smoke-test Setup on a clean Windows machine without the Visual C++ x64
runtime, and verify in-app discovery when a newer GitHub release is available.
Linux and Apple Silicon releases are deferred pending native testing and
dependency source review. See [TODO](todo.md) for the rest of the backlog.

The release tag remains on `4fa7a4e`. Later documentation edits apply to
`master`.
