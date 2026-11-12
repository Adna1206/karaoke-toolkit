# Current handoff

Snapshot: 2026-11-12, Africa/Johannesburg.

On `master` at `4866197`, added a manual Velopack Flow check under Help.
Startup checking was deferred to TODO. Before deferring it, the Windows
Release build and all four focused About/update tests passed with
`dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -m:1 -p:UsedAvaloniaProducts= -p:ArtifactBuildName=FlowCheck --filter 'FullyQualifiedName~KaraokeToolkit.Tests.App.UpdateServiceTests|FullyQualifiedName~KaraokeToolkit.Tests.App.About' -v minimal --logger trx --results-directory artifacts/tests/results`.
TRX: `artifacts/tests/results/almei_ANDRE_2026-11-12_16_01_26_net10.0.trx`.
The changes are uncommitted. The next verification is to install a build with
the check and query the same Flow channel after a newer release is available.

Rebuilt local Windows Flow test packages with manual checks only using
`./scripts/Publish.ps1 -RuntimeIdentifier win-x64` for baseline **2026.11.16**
(`20261112-161427-1cb260`) and dummy **2026.11.17** (`20261112-161507-d04b0f`).
Both are under `artifacts/releases/<version>/<run-id>/win-x64/`. Verified both
checksum lists. The .17 build needed a forced `win-x64` restore after NETSDK1047.
The shared source version is 2026.11.16. Flow created a .16 release record for
the earlier build, but the connection reset and the retry was canceled.
Flow then reported zero stored bytes and no latest release. The rebuilt
packages are local; the user will run the upload.

Local publish from `master` at `d2d0008` with the SoundTouch.Net TODO edit:
`./scripts/Publish.ps1` produced unsigned Windows x64 Setup and portable
packages and a Linux x64 AppImage for **2026.11.16** under
`artifacts/releases/2026.11.16/20261112-144754-ed3e9a/`. Verified both package
checksum lists and the published Windows CLI and FFmpeg version commands.
NuGet audit reported NU1900. Installation, GUI launch and native Linux
execution were not tested. Packages were produced locally.

After the first Windows Setup reported access denied, reran
`./scripts/Publish.ps1 -RuntimeIdentifier win-x64` outside the sandbox.
The unsigned rebuild is under `artifacts/releases/2026.11.16/20261112-150335-f2d10c/win-x64/`.
All package checksums matched and the published CLI reported 2026.11.16.
The installer has yet to be run.

## Checkout and verification

- Branch `master`, local base `da06388`, shared version **2026.11.16**.
  The base was one commit ahead of `origin/master` when checked. The working
  tree has documentation edits, including the filename cleanup and Tag to Name
  backlog entries. See the [changelog](changelogs/2026.11.16.md).
- The full Windows Release suite passed **709 tests** after the async
  SharpCompress migration, before the subsequent Avalonia, MCP SDK,
  Microsoft.Extensions and test tooling upgrades.
  TRX: `artifacts/tests/results/almei_ANDRE_2026-11-12_11_31_46_net10.0.trx`.
- After those dependency upgrades, the Release solution build passed with
  zero errors. NuGet could not reach the vulnerability feed (`NU1900`).
  The full suite still needs a rerun on the updated dependencies.
- Native notice preparation was checked for all four FFmpeg RIDs and Windows
  x64 cdrdao. Prepared notice files were compared against their source copies
  by SHA-256. About reads the prepared notices and credits Velopack.

```powershell
dotnet build KaraokeToolkit.slnx -c Release -m:1 -p:UsedAvaloniaProducts= -p:ArtifactBuildName=Cleanup -v minimal
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UsedAvaloniaProducts= -p:ArtifactBuildName=Cleanup -v minimal --logger trx --results-directory artifacts/tests/results -- xUnit.ParallelizeTestCollections=false
```

## Next work

- Recovered and checksum-pinned seven source snapshots at the exact revisions
  specified by the October BtbN recipe. Added the Mac recipe candidate, whose
  pinned versions agree with the vendor report. Its x264 download follows a
  moving branch. Obtain the exact Mac build inputs from the builder, or build
  a replacement with pinned inputs. For BtbN, recover LAME SVN r7113 and
  reconcile the remaining enabled dependencies, submodules and notices.
  The evidence bundle remains incomplete for publication.

- The offline distribution-source script now supports `-CdrdaoOnly` and both
  modes assembled successfully from cached archives. Both outputs include
  cdrdao provenance and notices; the full bundle also includes the TagLibSharp
  licence. Added the notice file from the restored Microsoft.Extensions 11.0.0
  packages and verified App inclusion using `dotnet msbuild -getItem:None`.
  These script, notice and documentation changes are uncommitted.
- Windows and Linux now pin the retained October 31 BtbN 8.1 LGPL shared
  release, verified against the upstream checksums. The old archives are
  under `artifacts/checks/ffmpeg-review/september-29/`. Both new archives
  passed preparation on Windows; Windows FFmpeg and FFprobe reported the new
  version. Linux execution still needs native verification. The offline source
  bundle now includes the new FFmpeg source
  and exact BtbN recipe commit. Matching external-library sources and notices
  are still pending.
- Apple Silicon now uses Martin Riedl's 9.0.2 ZIPs. Both archive checksums
  match upstream, both executables have ARM64 Mach-O headers, and
  `scripts/ffmpeg/Prepare.ps1 -RuntimeIdentifier osx-arm64` passed on Windows.
  The old OSXExperts files are in `artifacts/checks/ffmpeg-review/osx-arm64-osxexperts/`.
  Retired the Intel runtime and CI leg. The Evermeet files are under
  `artifacts/checks/ffmpeg-review/osx-x64-evermeet/`. After cleanup,
  `scripts/ffmpeg/Prepare.ps1` passed on Windows for all three retained RIDs,
  and `git diff --check` passed. Native Apple Silicon execution and signing
  checks, the exact recipe revision and complete corresponding source remain open.
- Windows Release audio/video regression tests passed **54/54** with the
  October build. The first sandboxed run built successfully but timed out
  connecting to testhost. The rerun used
  `dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-build --no-restore -p:UsedAvaloniaProducts= -p:ArtifactBuildName=Cleanup --filter 'FullyQualifiedName~KaraokeToolkit.Tests.Audio|FullyQualifiedName~KaraokeToolkit.Tests.Video' --logger trx --results-directory artifacts/tests/results`.
  TRX: `artifacts/tests/results/almei_ANDRE_2026-11-12_13_13_39_net10.0.trx`.

- Before public distribution, resolve corresponding source for downloadable
  FFmpeg builds and arrange source access for other bundled copyleft
  components. See [distribution licensing](distribution-licensing.md).
- Run the full suite on the current dependencies and smoke-test any packages
  selected for release. Linux packages are cross-built; macOS packaging and
  physical disc-ripping verification remain open.
- See [TODO](todo.md) for the FFMpegCore migration and other planned features.
  The backlog now includes native vendoring investigation and Velopack update UI.
  See [updates](updates.md) for the manual Flow check and remaining update work.
