# Current handoff

## GitHub Releases update checks

On 2026-11-16, master at `952c6dc`, version 2026.11.17, pointed Velopack
at `Adna1206/karaoke-toolkit` GitHub Releases. The client checks anonymously
and includes published prereleases for its installed channel.
`scripts/Test.ps1 -NoRestore -Filter 'FullyQualifiedName~UpdateServiceTests|FullyQualifiedName~PreferenceTests'`
passed 20/20. GitHub returned 404 for the repository lookup with the available
access, so live update discovery remains unverified. Build and install the
GitHub-enabled version, then test discovery against a newer published release
with the matching channel assets. No repository visibility or release changes
were made. See [updates](distribution/updates.md) and [publishing](distribution/publishing.md).

On 2026-11-16, master at `952c6dc`, version 2026.11.17, labelled the
existing bundled OpenH264 BSD-2-Clause text in About → Third-party notices.
`scripts/Test.ps1 -NoRestore -Filter 'FullyQualifiedName~AboutViewModelTests'`
passed 4/4. The label change is uncommitted and awaits the next installer build.
The changelog and publishing guide now cover the label and the clean Windows
Setup check. The next step is to commit and tag the release inputs, rebuild,
and verify Setup on a clean machine.

## Windows tester source package

On 2026-11-16, master at `952c6dc`, version 2026.11.17, collected the
checksum-pinned Windows FFmpeg dependency sources, patches and package recipes.
The source archive includes all 357 Rust registry inputs from the pinned
librsvg release lockfile. The [distribution record](distribution/distribution-licensing.md#remaining-ffmpeg-work)
lists the scope. `scripts/Prepare-DistributionSources.ps1 -Archive` verified
the 582-file ZIP under `artifacts/distribution-sources/` with prefix
`20261116-054802-a35fc4`. The separate Toolkit source snapshot with that
prefix verified 342 files. `scripts/Publish.ps1 -RuntimeIdentifier win-x64`
rebuilt the unsigned installer under release run `20261116-054827-91429c`.
Clean Windows setup and Linux/macOS native runtime testing
remain open. Linux and macOS dependency source assembly is still pending.
Updated the 2026.11.17 changelog and documented the release assets. For the
Windows test release, commit and tag the build inputs, rebuild from the tag,
smoke-test the installer and share the reviewed third-party source ZIP. Link
the matching Toolkit tag where all installer recipients can download it.
No changes were committed and no artifacts were uploaded.

## Conda-forge FFmpeg replacement

On 2026-11-16, master at `952c6dc`, version 2026.11.17, replaced the BtbN
and Martin Riedl runtimes with Conda-forge FFmpeg 9.0.2 LGPL for Windows x64,
Linux x64 and Apple Silicon. Retired the previous build paths, pins, maintained
notices and unpacked runtime caches. Preparation verifies the archives, each
shipped binary and the package notices. The Windows setup declares Visual C++
x64 as a prerequisite. Added a synthetic VP9/Opus fixture and encoded-stream
hash checks, and switched to direct FFprobe JSON reads to fix truncated output
seen in parallel tests.

- `scripts/Test.ps1 -NoRestore` passed 741/741 tests on Windows with the
  default runtime. NuGet vulnerability metadata was unavailable. TRX under
  `artifacts/tests/results/` begins `20261116-053659-50e93d`.
- `scripts/Prefetch-Runtimes.ps1 -RuntimeIdentifier win-x64,linux-x64,osx-arm64`
  verified the warm cache. Cross-prepared and independently verified 61 Windows,
  97 Linux and 75 macOS files, with an installed notice for each locked package.
- `scripts/Publish.ps1` produced the unsigned Windows installer and
  cross-built Linux AppImage under
  `artifacts/releases/2026.11.17/20261116-053738-4b0fb7/`.
- `scripts/Prepare-DistributionSources.ps1 -Archive` verified the 69-file
  evidence ZIP at `artifacts/distribution-sources/20261116-054124-9e9099-third-party-sources-2026.11.17-incomplete.zip`.
  It includes upstream FFmpeg source and the three embedded build recipes.

Changes are uncommitted. Nothing was installed or uploaded. Next, run a clean
Windows setup smoke test and native Linux/macOS media tests.
See [distribution licensing](distribution/distribution-licensing.md#remaining-ffmpeg-work).

## Windows FFmpeg source gap review

On 2026-11-16, master at `952c6dc`, version 2026.11.17, added 35 source pins
covering AMF headers, MinGW-w64, OpenCORE AMR, SoXR, nested inputs and crates
identified in the Windows codec DLL and verified against rav1e's lockfile.
Added three Windows FFmpeg notice texts and verified all three by SHA-256
in the output of `scripts/ffmpeg/Prepare.ps1` with `-RuntimeIdentifier win-x64`.
`pwsh -NoProfile -File scripts/Prepare-DistributionSources.ps1 -Archive`
verified 168 ZIP entries in
`artifacts/distribution-sources/20261116-013415-60c23e-third-party-sources-2026.11.17-incomplete.zip`.
`git diff --check` passed. Changes are uncommitted. Exact resolution of
nested and generated inputs and the complete licence notice review remain
open. No repackaged installer or source archive was uploaded.

## Public source and Windows package review

On 2026-11-16, master at `1205c2a`, version 2026.11.17, audited the replacement
repository before a possible visibility change. Authenticated GitHub API
inspection found only master and zero tags, issues, PRs, releases, Actions
runs or artifacts. Scanned 1,022 blobs reachable from origin/master and
commit messages with a local credential-pattern check. No credential matches
were found. One non-noreply address occurs in Git commit metadata. Confirm
its publication with the owner before changing visibility. This was a
pattern scan, not a complete manual audit. The owner subsequently chose to
replace that address with GitHub's noreply identity in all 39 commits.
Verified commit trees, messages and dates, then replaced GitHub master with
a force-with-lease push. Local and remote master now point to `952c6dc`.
The repository uses noreply for future commits. The original-history bundle
and isolated rewrite repository were removed at the owner's request.
Visibility is unchanged. Private
vulnerability reporting is recorded in [TODO](todo.md) for after launch.

`pwsh -NoProfile -File scripts/Publish.ps1 -RuntimeIdentifier win-x64` rebuilt
unsigned packages under `artifacts/releases/2026.11.17/20261116-011028-3cb4dc/win-x64`
and verified their checksums. The extracted portable package reported CLI
version 2026.11.17 and produced a probed H.264/AAC MP4. CDRDAO printed 1.2.6
and usage outside the sandbox. Installer execution, hardware and upload
remain unverified. GitHub Releases can host the manual download. The FFmpeg
corresponding-source gaps still block treating the source archive as complete.
This documentation is uncommitted.

## Recovery path-check performance

On 2026-11-16, master at `b3f9b09`, version 2026.11.17, reduced recovery
path safety checks to one attribute query per path component and reused the
leaf attributes during capture and verification. Ancestor checks and fresh
content hashes remain in place. Recovery changes and the documentation
reorganization are being committed separately on master.

`pwsh -NoProfile -File scripts/Test.ps1 -NoRestore` passed 741 Windows tests
with local test-host access. The first run had one discovery miss immediately
after killing a conversion worker. The test now waits for discovery within
its existing timeout. The isolated comparison in
`artifacts/checks/recovery-phases/Phases.csproj -- --compare` compared 2,000
small files twice and produced identical fingerprint maps in 1.4–1.5 seconds
versus 3.8–4.0 seconds before. Separate 10,000-file timings varied with system
load. No cross-platform timing or larger-file throughput was measured.
Revisit further tuning only if real workloads still show a material delay.

## Recovery discovery and profiling

On 2026-11-16, master at `b3f9b09`, version 2026.11.17, combined scoped
conversion and workflow journal discovery into one tree walk and removed the
duplicate startup index pass. Changes are uncommitted with the earlier recipe
and docs work. The focused command
`pwsh -NoProfile -File scripts/Test.ps1 -NoRestore -Filter 'FullyQualifiedName~Recovery|FullyQualifiedName~Recipes|FullyQualifiedName~Flatten'`
passed 132 tests with local test-host access. `git diff --check` passed.

The local `artifacts/checks/recovery-phases/Phases.csproj` harness used two
10,000-file fixtures. Median discovery fell from 6 to 2 ms on the flat fixture
and 484 to 319 ms on the nested one. SHA-256 capture, including path checks,
took 19–26 seconds. A synthetic checkpoint write took 16–91 ms. A controlled
Windows lock reproduced sharing violation 32 during the atomic rename. The
source remained in place and the rename worked after releasing the lock.
Next, isolate path-check costs from hashing before changing capture. Consider
a bounded retry at the atomic rename if brief locks are a real usage problem.

## Recipe checkpoint and input selection

On 2026-11-16, master at `b3f9b09`, version 2026.11.17, saved completed
recipe checkpoints before replacing live state and preserved operation
records when saving fails. Recovery honors a saved stop after reporting is
interrupted. Indexed moved-file results by source and computed desktop
status without flattening step results. These changes and the earlier docs
reorganization are uncommitted.

`pwsh -NoProfile -File scripts/Test.ps1 -NoRestore -Filter 'FullyQualifiedName~Recipes|FullyQualifiedName~Recovery|FullyQualifiedName~RunStatus'`
passed 130 tests with local test-host access after the sandbox connection
timed out. A local 10,000-file synthetic selector comparison returned
identical inputs in 1.6 seconds with indexing versus 2.5 seconds before.
The two-step synthetic recipe took 82 seconds with real filesystem checks
and checkpoints and a fixture executor. Creating its report took 7 ms.
This did not exercise media processing or desktop rendering. Profile
recovery scans and fingerprinting separately before changing verification
or adding retries.

## Documentation layout and community guides

On 2026-11-16, master at `b3f9b09`, version 2026.11.17, grouped the guides
under user, development, distribution and research. Added a README Quick
Start, community guides and a roadmap linked from the [docs index](README.md).
Updated repository links and source archive paths. Fixed a PowerShell variable
collision affecting CDRDAO-only source preparation. Local Markdown targets
and `git diff --check` passed. Running
`pwsh -NoProfile -File scripts/Prepare-DistributionSources.ps1 -CdrdaoOnly -Archive`
verified 21 ZIP entries. Changes are uncommitted. A public installer URL and
private reporting contact remain to be established before public launch.

## Windows FFmpeg source review

On 2026-11-15, master at `9f9dc76`, version 2026.11.17, checked the
pinned Windows FFmpeg DLL and evaluated the BtbN recipe for win64 LGPL 8.1.
Identified embedded libbluray 1.5.1, the expected external libudfread pin,
and LAME SVN r7113. Archived the 477-file LAME revision and 66 additional
recipe inputs. The offline `Prepare-DistributionSources.ps1 -Archive`
run verified 130 ZIP entries at
`artifacts/distribution-sources/20261115-170305-8d4c56-third-party-sources-2026.11.17-incomplete.zip`.
AMF, mingw-w64, opencore-amr and soxr archives, nested inputs and notice
review remain open. The manifest and docs changes are uncommitted.

## Third-party source archive

On 2026-11-15, master at `9f9dc76`, version 2026.11.17, added `-Archive` to
`scripts/Prepare-DistributionSources.ps1`. User selected a separate source asset
for GitHub releases. The offline command
`pwsh -NoProfile -File scripts/Prepare-DistributionSources.ps1 -Archive` passed:
36 pinned source inputs, 63 ZIP files, all lengths and SHA-256 values checked.
Output: `artifacts/distribution-sources/20261115-024105-4683b0-third-party-sources-2026.11.17-incomplete.zip`
with a SHA-256 sidecar. Includes CDRDAO/runtime, TagLibSharp and current FFmpeg
source evidence across platforms. Not uploaded; FFmpeg source gaps remain.
Script/docs are uncommitted alongside the earlier startup-update work. Next:
finish the FFmpeg source inventory before using this as a complete release asset.

## Source availability register

On 2026-11-15, master at `9f9dc76`, version 2026.11.17, added
[source availability](distribution/source-availability.md) with component versions, upstream
source links and explicit provenance gaps. Linked it from the docs index,
licence inventory and distribution guide. This is documentation only; no source
hosting, download page or packaging changes. Existing startup-update changes
remain uncommitted. Checked local Markdown links and `git diff --check`; no
runtime tests needed. Next: resolve source matches and public access arrangements
before presenting the register as complete on a public download page.

## Startup update preference

On 2026-11-15, added the opt-in General setting for 2026.11.17. Startup checks
run after the main window opens and recovery handling finishes. Only available
updates produce a startup dialog. Windows Release passed **55/55** with
`scripts/Test.ps1 -NoRestore -Filter 'FullyQualifiedName~App.Settings|FullyQualifiedName~UpdateService'`.
An installed startup check against a newer Flow release remains to be verified.
`pwsh -NoProfile -File scripts/Publish.ps1 -RuntimeIdentifier win-x64` produced
unsigned Windows packages under `artifacts/releases/2026.11.17/20261115-020000-d06b95/win-x64`.
Package checksums were verified. Installation has not been tested. Flow upload
via `dotnet tool run vpk -- publish --outputDir <release-dir> --channel win --waitForLive`
reset during transfer twice, then reset with just Setup and the full update package.
Flow still reports draft `af66501c-5f76-4a15-cef5-08df40a0b8e1` as AwaitingUploads
with zero bytes. No HTTP size-limit response was observed.

## Native runtime archive migration

On 2026-11-15, version 2026.11.17, moved the native runtime archives from
Git to a SHA-256-keyed download cache. All six exact upstream archives
downloaded successfully into an empty cache. Verified runtime preparation
for win-x64, linux-x64 and osx-arm64 on Windows, Windows PowerShell cold
download and cache reuse, and both source evidence assembly paths.
`pwsh -NoProfile -File scripts/Test.ps1` passed **735/735** on Windows.
The cache and checksum-verified backup copies are in ignored
`artifacts/runtime/downloads` and `artifacts/runtime-backup`.
Native Linux/macOS execution and public distribution review remain open.

## 2026.11.17 release checkpoint

Prepared on master from base `6237ae5`. See [release notes](changelogs/2026.11.17.md)
for the feature scope. Full Windows Release verification via
`pwsh -NoProfile -File scripts/Test.ps1` passed **735/735**. TRX:
`artifacts/tests/results/20261115-010633-00f6d78c94cf46be829202e31bbc0805_net10.0_20261115010704.trx`.
The separate distribution evidence update verified all 36 pinned archives
using `./scripts/Prepare-DistributionSources.ps1` without downloads. Output:
`artifacts/distribution-sources/20261115-011143-f1f627`. Public distribution
review remains open. Next release step is to build and smoke-test 2026.11.17 packages.

## Artifact retention

Routine tests now use the reusable StandaloneTests output. The Windows run
`pwsh -NoProfile -File scripts/Test.ps1 -NoRestore -Filter FullyQualifiedName~TrackGrouping`
passed **23/23**, retained its TRX report and removed its fixtures. Synthetic
retention checks removed expired marked runs and preserved unmarked runs.
`pwsh -NoProfile -File scripts/Publish.ps1 -RuntimeIdentifier win-x64` produced
unsigned packages in `artifacts/releases/2026.11.16/20261115-010344-8b1217/win-x64`
and removed staging after verifying checksums. Linux packaging, installation
and package execution were not tested in this run.

## Folder tool labels

Aligned the desktop track grouping, alphabetical grouping and flatten labels and descriptions. Windows App Release build and Git diff checks passed.

## Desktop views and settings naming

MainWindow hosts OperationPickerView for home category and operation navigation,
and supplies its source panel. Windows Release passed **53/53** using output
name OperationPicker and filter "FullyQualifiedName~App.Main". Live keyboard
navigation and source picker placement remain to be checked.

MainWindow hosts OperationProgressView, which includes ActivityLogView.
Desktop OptionsWindow and OptionsViewModel are now SettingsWindow and
SettingsViewModel. Windows Release passed **114/114** using output name
SettingsView and filter `FullyQualifiedName~App.Settings|FullyQualifiedName~App.Main|FullyQualifiedName~RecipeBuilder`.
Live layout and scrolling remain to be checked.

## Desktop activity table

Extracted the live table into ActivityLogView with view-owned auto-scroll.
See [App structure](development/app-structure.md). The Windows Release App build passed.
MainWindow visibility falls back to the idle layout when the designer has no
view model. The standalone ActivityLogView provides design-only sample rows.
The Windows Release App build passed with zero warnings or errors. Live
scrolling and the Visual Studio preview still need verification.

## Recovery verification settings

Transaction journals now record their verification mode. A missing field
means SHA-256. Explicit xxHash128 transaction recovery uses Microsoft's
System.IO.Hashing package. The two Windows journal compatibility cases passed
with output name RecoveryModes and filter
`FullyQualifiedName~TransactionRecoveryReadsLegacySha256AndExplicitXxHash128`.

Workflow journals also retain their verification mode and default to SHA-256
when it is omitted. Core operation requests can select xxHash128 for track
grouping, alphabetical organization and flattening. The targeted Windows
Release suite passed **118/118** using RecoveryModes and filter
`FullyQualifiedName~TrackGrouping|FullyQualifiedName~Recovery|FullyQualifiedName~FlattenFolderLayer|FullyQualifiedName~AlphabeticalOrganizer`.

Desktop Settings → Recovery and CLI/MCP expose SHA-256, xxHash128, metadata only,
no verification and an option to disable automatic batch resume for standalone
grouping, alphabetical organization and flattening. SHA-256 is the default.
See [recovery verification](user/crash-recovery.md#verification-settings). Other
recovery and publication paths retain their existing SHA-256 behavior. The
Windows Release suite passed **732/732** before the final manual-review test
and license additions. The final targeted Release run passed **234/234**.

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:UsedAvaloniaProducts= -p:ArtifactBuildName=RecoveryModesFinal -v minimal --filter 'FullyQualifiedName~TrackGrouping|FullyQualifiedName~Recovery|FullyQualifiedName~Settings|FullyQualifiedName~CliWorkflow|FullyQualifiedName~McpTests' -- xUnit.ParallelizeTestCollections=false
```

Next, verify the Recovery settings tab live on Windows and exercise the
file-move modes on native Linux and macOS.

## File move recovery and activity wording · 2026-11-15

Standalone flattening and alphabetical organization use saved SHA-256 move
plans. Desktop shows preparation in the activity log without an operation warning. See [crash recovery](user/crash-recovery.md)
and [activity table messages](development/activity-messages.md). The Windows Release run
passed **58/58** using the GroupingFinal command below without logger arguments
and with `--filter 'FullyQualifiedName~FileOperations|FullyQualifiedName~WorkflowRecovery|FullyQualifiedName~OperationFlow'`.
The dialog and native Linux/macOS execution still need live verification.

## Group by Track ID · 2026-11-14

Implemented on `master` at base `6237ae5`, version **2026.11.16**. The
uncommitted feature adds desktop setup and preview, CLI and MCP dispatch, a
shared interval planner, and resumable copy and move publication. See
[Group by Track ID](user/track-grouping.md). Existing distribution evidence edits
remain in the working tree. Move requires a same-volume destination. Live
desktop layout and native Linux/macOS execution remain to be checked.

The desktop dialog now defaults to Move, uses dividers between settings
groups, and displays the literal hyphen in the range separator dropdown.
The app-wide blended title bar style applies on Windows. The focused
desktop default-move workflow passed 1/1 using the command below with
`--filter FullyQualifiedName~TrackGroupingKeepsItsSelection` in place of
the logger, results directory and xUnit arguments.

Grouping now shows plan checking and recovery preparation before item rows.
The desktop preselects the selected scan folder or the shared containing folder.
Grouping logs Processing rows with relative paths and shows the preview issues
section only when there are skipped items or conflicts. Move and Copy use the app's accent style when ready. The App Release build passed with zero warnings or errors using the GroupingFinal output name. The colour was not checked live.
The focused Release run passed **14/14** with "--filter FullyQualifiedName~TrackGrouping".

The activity audit aligned organization, flattening, validation, tagging,
disc capture and recipe move steps with the conversion Processing label.
The Windows Release regression run passed **214/214** using the command below
without logger arguments and with the filter
`FullyQualifiedName~FileOperations|FullyQualifiedName~Tagging|FullyQualifiedName~Validation|FullyQualifiedName~DiscRip|FullyQualifiedName~Recipe|FullyQualifiedName~OperationFlow|FullyQualifiedName~WorkflowRecovery`.
Disc capture used test readers.

Windows Release passed **723/723** outside the sandbox. The sandboxed run
built successfully and timed out connecting to testhost.

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:UsedAvaloniaProducts= -p:ArtifactBuildName=GroupingFinal -v minimal --logger trx --results-directory artifacts/tests/results -- xUnit.ParallelizeTestCollections=false
```

TRX: `artifacts/tests/results/almei_ANDRE_2026-11-14_23_26_08_net10.0.trx`.

Snapshot: 2026-11-12, Africa/Johannesburg.

FFmpeg source tracing on `master` at `6237ae5` added 19 checksum-pinned
archives to the source manifest, including the two libudfread revisions and
three Mac archives matching the reported versions. Confirmed the LAME r7113
SVN tree is accessible. The Mac recipe and x264 revision still need builder
confirmation. The BtbN per-target dependency and notice review remains open.

On `master` at `4866197`, added a manual Velopack Flow check under Help.
Startup checking was deferred to TODO. Before deferring it, the Windows
Release build and all four focused About/update tests passed with
`dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -m:1 -p:UsedAvaloniaProducts= -p:ArtifactBuildName=FlowCheck --filter 'FullyQualifiedName~KaraokeToolkit.Tests.App.UpdateServiceTests|FullyQualifiedName~KaraokeToolkit.Tests.App.About' -v minimal --logger trx --results-directory artifacts/tests/results`.
TRX: `artifacts/tests/results/almei_ANDRE_2026-11-12_16_01_26_net10.0.trx`.
The manual check is committed at `6237ae5`. The next verification is to install a build with
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
  The newer suite results are recorded above.
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
  components. See [distribution licensing](distribution/distribution-licensing.md).
- Run the full suite after the activity and readiness changes, inspect the grouping dialog live, and smoke-test any packages selected for release. Linux packages are cross-built; macOS packaging and
  physical disc-ripping verification remain open.
- See [TODO](todo.md) for the FFMpegCore migration and other planned features.
  The backlog now includes native vendoring investigation and Velopack update UI.
  See [updates](distribution/updates.md) for the manual Flow check and remaining update work.
