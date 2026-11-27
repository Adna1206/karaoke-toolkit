# Current handoff

## FFMpegCore 6.0.0

2026-11-27, `master`, base `448c8e8`, version 2026.11.18. This commit
upgrades the managed FFMpegCore package from 5.5.0 to 6.0.0. Audio gain uses the
new process result for stderr and success, the stdout callback for progress,
and null output for loudness measurement. Audio conversions explicitly set a
zero cancellation grace period for disposable staged outputs. See the
[upstream migration guide](https://github.com/rosenbjerg/FFMpegCore/blob/v6.0.0/MIGRATION.md).
The bundled native runtime reports FFmpeg 9.0.2. Updated and checked the MIT
licence in the test output against the source revision recorded by NuGet.

With SDK 10.0.404, `dotnet build KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj
-c Release -m:1 -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTestsFFMpegCore6`
passed with zero warnings. The same test DLL passed all 944 Windows tests with
`-parallelMode none -longRunning 20 -noColor`, including loudness measurement
with a file named `-` present in the working directory.

## SharpCompress 0.50.12

2026-11-27, `master`, base `63defa9`, version 2026.11.18. This dependency
commit upgrades SharpCompress from 0.50.8 to 0.50.12. Staged ZIP extraction
and verification, ZIP tagging,
and audio gain replacement reads now pass async file streams to those APIs.
The [licence inventory](../licenses/README.md) records the package version.
The MIT text matches the source revision identified by the package.

With SDK 10.0.404, `dotnet build KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj
-c Release -m:1 -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTestsSharpCompress`
passed with zero warnings. Verified the licence in build output. The test DLL
passed all 944 Windows tests with `-parallelMode none -longRunning 20 -noColor`.

## Hashing and Coverlet updates

The dependency commit also updates System.IO.Hashing to 11.0.0 and Coverlet Collector to
10.1.1. Updated the bundled hashing licence and notices and verified both files
in build output. The .NET 10 versions of the old and new hashing libraries gave
identical xxHash128 fingerprints for 17 synthetic input sizes.

Coverage is opt-in. The shared runsettings excludes `*.axaml` from collection
because generated markup symbol names can contain characters that Cobertura
cannot encode. See [Test structure](development/test-structure.md).

With SDK 10.0.404, `dotnet build KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj
-c Release -m:1 -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTestsHashingCoverlet`
passed with zero warnings. `dotnet test` with `--collect "XPlat Code Coverage"`
and the `FileTransactionTests` filter passed six tests and produced a Cobertura
report with 203 covered lines. Running the same test DLL with
`-parallelMode none -longRunning 20 -noColor` passed all 944 Windows tests.

## Batch recovery and dependency checkpoint

2026-11-27, `master`, base `5ad749e`, version 2026.11.18. This commit
collects the recovery defaults and checkpoint optimizations, cancellable
conversion scans, and xUnit and Avalonia upgrades described below. The Windows
Release test run passed all 944 tests across two VSTest filters. Debug and
Release desktop builds passed with zero warnings. Older standalone move and
grouping-copy journals require the version that created them. Complete any
such interrupted runs before upgrading. See [Crash recovery](user/crash-recovery.md).

## Avalonia 12.2.0

The App project pins Avalonia, Desktop, Fluent and Inter to 12.2.0.
DiagnosticsSupport is at 2.2.3 for Debug. Updated the
[licence inventory](../licenses/README.md) for Avalonia and MicroCom.Runtime
0.11.7 and checked the licence text and bundled Inter font files upstream.

With SDK 10.0.404, `dotnet build KaraokeToolkit.App/KaraokeToolkit.App.csproj`
passed in Debug and Release with `-m:1 -p:UsedAvaloniaProducts=` and artifact
names `Avalonia122Debug` and `Avalonia122Release`. Both builds reported zero
warnings. Verified the licence files in Release output and the diagnostics
assembly in Debug output. The preceding 944-test run used the same Avalonia
versions.

## xUnit v3 migration

`KaraokeToolkit.Tests` uses `xunit.v3` 4.0.3 with the
existing VSTest adapter 4.0.1. The test executable handles its recovery, disc
process and benchmark switches before handing other arguments to xUnit.
Cancellation-token guidance from xUnit1051 appears as an IDE suggestion.
See [Test structure](development/test-structure.md).

Using SDK 10.0.404, `dotnet build KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj
-c Release -m:1 -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTestsXunitV3`
passed with zero warnings. `dotnet test` used the same Release output with
`--no-build --no-restore`, serial execution, and a 60-second inactivity limit.
Separate VSTest filters passed all 48 Core tagging tests and all 896 remaining
tests. The direct xUnit runner previously passed 944/944. Earlier single-invocation
VSTest runs encountered tagging timeouts and one settings-file replacement error.
The two orphaned tagging test runners and their FFprobe children have exited.

## Conversion source scans

Prepared and resumed conversions use the saved source list. Fresh conversion
scans check for cancellation between directory entries and animate the desktop
footer progress bar until the run is ready to show normal progress. The source
list is saved before processing. A blocked directory read must return before
the scan can stop. See [Conversion progress](user/file-workflow-features.md#conversion-progress).

The StandaloneTests Release build and serial Windows test commands under
[KMA to MP3+G](#kma-to-mp3g) passed with zero build warnings and 944/944 tests.
Coverage includes scan cancellation, initial batch totals, and saved-source resume.

## Conversion performance and recovery

Conversions skip known output collisions under **Skip existing outputs**,
append checksummed standalone batch progress, and reuse final commit fingerprints
when building backup records. Deferred overwrite review and published output
verification still recheck the files. See [Recovery](user/crash-recovery.md).

Synthetic ZIP to Audio+G benchmarks on Windows used xxHash128 and three measured
runs after warm-up. Median times for each isolated change were 5.00 to 0.47 s for
90 known conflicts, 11.00 to 9.48 s for 240 small conversions, and 3.89 to 3.81 s
for 80 replacements. The replacement timings overlapped and the change removed
640 MiB of duplicate audio reads per batch. With all changes, the same workloads
took 0.11, 9.10 and 3.81 s. Raw timings are in
`artifacts/benchmarks/conversion-recovery-b8a494ef`.

The StandaloneTests Release build and serial Windows test commands under
[KMA to MP3+G](#kma-to-mp3g) passed with zero build warnings and 940/940 tests.
Coverage includes early conflict skips, replay after a torn checkpoint, and
rejection of changes made after a file was observed for publication.

## Grouping copy recovery

Track ID grouping copies use the shared checksummed group log. They save verified staged
fingerprints before publishing and replay prepared and completed records on
recovery. Pre-publication checks share ancestor probes within the group. Older
grouping copy journals require their original version. See
[Recovery](user/crash-recovery.md).

The same-drive Windows benchmark used three warm-cache runs per build with
xxHash128 and BeforeItem timing. The median for 120 pairs totaling 965.6 MiB
fell from 7.04 s to 4.90 s. For 240 pairs totaling 26.25 MiB it fell from
11.02 s to 6.80 s. Raw timings are in
`artifacts/benchmarks/grouping-copy-recovery-5d20dd58`.

The StandaloneTests Release build and serial Windows test commands under
[KMA to MP3+G](#kma-to-mp3g) passed with zero build warnings and 938/938 tests.
Coverage includes interrupted staging, partially published copies, and legacy
journal rejection.

## Same-volume move performance

Standalone moves and renames save the plan and append checksummed group
checkpoints. Recovery
replays the log and discards a torn final record. Older standalone move
journals require their original version. Per-group preflight reuses ancestor
checks and avoids a redundant source path check. See [Recovery](user/crash-recovery.md).

Warm-cache Windows medians for the same synthetic MP3/CDG move workload and
Metadata only verification, with three runs per phase:

| Pairs | Full journal rewrites | Per-group journal | Shared path checks |
| --- | ---: | ---: | ---: |
| 120 | 1.82 s | 1.16 s | 0.62 s |
| 480 | 8.12 s | 4.25 s | 2.52 s |

The baseline used the earlier instrumented Core build. The two phases used the
same benchmark runner under `artifacts/benchmarks/move-optimization-20261126`.
Production library timing remains unmeasured.

With both optimizations active, three warm-cache runs per mode measured
xxHash128 versus Metadata only at 2.93 s versus 2.89 s for 480 pairs totaling
52.5 MiB and 4.33 s versus 2.94 s for 480 pairs totaling 3.77 GiB. Raw timings
are under `artifacts/benchmarks/optimized-verification-65f34221`.

The StandaloneTests Release build and serial Windows test commands under
[KMA to MP3+G](#kma-to-mp3g) passed with zero build warnings and 937/937 tests.
Tests covered torn journal tails, partial moves, and occupied destinations.
After removing older standalone move journal support, the same Release build
and the test command with
`--filter 'FullyQualifiedName~Recovery|FullyQualifiedName~TrackGrouping|FullyQualifiedName~FilenameRename|FullyQualifiedName~FlattenFolderLayer|FullyQualifiedName~AlphabeticalOrganizer'`
passed with zero build warnings and 212/212 tests.

## Recovery verification defaults

Recovery settings have separate verification controls for same-drive moves and renames
(Metadata only) and copies and file writes (xxHash128). Desktop, CLI, and
MCP share both preferences. Group by Track ID selects the preference for its
Move or Copy mode. Metadata-only same-volume moves can recover with matching
fingerprints. See [Recovery settings](user/crash-recovery.md#recovery-settings).

The StandaloneTests Release build and serial Windows test commands under
[KMA to MP3+G](#kma-to-mp3g) passed with zero build warnings and 937/937 tests.
The live Recovery tab showed both defaults and their muted coverage text.
A warm-cache Windows benchmark applied the same Track ID move plan for 120
synthetic MP3/CDG pairs three times per mode with BeforeItem timing. Median
xxHash128 versus Metadata only was 1.75 s versus 1.77 s for 13.1 MiB and
1.99 s versus 1.72 s for 965.6 MiB. A production library remains unmeasured.

An instrumented copy of Core measured a 480-track metadata-only move at
8.04 s across two warm runs. Journal writes took 3.28 s, per-group preflight
took 2.85 s, and the 960 native renames took 0.91 s. The run wrote 530 MiB
of journal JSON across 963 saves. The diagnostic build lives under ignored
`artifacts/benchmarks/move-checkpoints-9aff2d1a`.

## Desktop workflow checkpoint

2026-11-26, `master` based on `9dfdd1d`, version 2026.11.18.

- Activity reports completed items out of the total and shows elapsed time,
  remaining time and throughput. See [Activity table messages](development/activity-messages.md).
- The home sidebar uses **Names & Tags**, **Audio Tools**, and **Organization**.
  Each category has one display definition and favourites are saved by operation ID.
- Track ID grouping, alphabetical grouping, filename renaming and tagging rebuild
  previews in the background after a 225 ms pause. New edits cancel older work.
- **Group by Alphabet → Files** can skip a leading Track ID using a preset or
  custom separator. The approved move uses the same destination as the preview.
  See [Group files and folders](user/file-workflow-features.md#group-files-and-folders).
- The activity log and shared operation previews use the same table styling.
  Track ID grouping retains its expandable tree.

Live Windows checks covered the alphabetical results and conflict table, the
activity log, Track ID tree expansion, and the dash, period and whitespace
separator behavior. Small-window rename and audio visuals and large-library
preview responsiveness remain unmeasured.

The StandaloneTests Release build and serial Windows test commands under
[KMA to MP3+G](#kma-to-mp3g) passed with zero build warnings and 935/935 tests.

## Track ID grouping width

2026-11-24, `master` from `d3f770b`, version 2026.11.18. This checkpoint adds
**Minimum ID digits** to the [Group by Track ID preview](user/track-grouping.md),
`--minimum-id-digits` to CLI, and `minimumIdDigits` to the MCP grouping request.
The default uses source width. A positive width normalizes leading zeroes in
generated group and parent names for files and whole folders. Existing recovery
plans already store concrete destination paths.

The StandaloneTests Release build command below passed with zero warnings. The
serial Windows `dotnet test` command below, with `--filter
'FullyQualifiedName~TrackGrouping|FullyQualifiedName~OperationFlowTests|FullyQualifiedName~CliWorkflowTests|FullyQualifiedName~McpTests'`,
passed 111/111 with local test-host access. The desktop control was checked by
compiled markup and a preview view-model test. The live window remains unverified.
Next, try the option on a representative folder batch when available.

## KMA to MP3+G

2026-11-24, `master` from `bc8e768`, version 2026.11.18. This checkpoint adds
**Convert KMA to MP3+G** in Desktop, CLI and MCP. Core handles interleaved WMA/CDG,
separator-based WMA or MP3 with CDG, and embedded karaoke ZIPs. It keeps the KMA
and publishes the pair together, preserving CDG and embedded MP3 bytes. The reader
checks competing separators, tolerates bounded zero padding on MP3 and ZIP
layouts, and retains graphics and sync warnings through overwrite review and
recovery. See [Convert KMA to MP3+G](user/file-workflow-features.md#convert-kma-to-mp3g).

`dotnet build KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release
--no-restore -m:1 -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests`
passed with zero warnings. With local test-host access, `dotnet test
KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-build --no-restore
-p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests --blame-hang-timeout 25s
--blame-hang-dump-type none --results-directory artifacts/tests/results
-- xUnit.ParallelizeTestCollections=false` passed 910/910 on Windows. The 22 KMA
cases use generated fixtures to check the five karaoke type values, legacy
headers, malformed and ambiguous boundaries, padding, warnings, and the Desktop
workflow. A CLI dry run listed both output paths. CLI `recover` resumed a
disposable conversion and preserved a tagged MP3 and its KMA byte-for-byte.
Real KMA files, lyric playback and non-Windows runs remain unverified. Next,
verify real samples from each layout when available.

## Audio tools checkpoint

2026-11-23, `master`, version 2026.11.18. The commit containing this checkpoint
records the work after `addb2d7`: batch audio gain and key changes in Desktop,
CLI and MCP, single-track karaoke ZIP support, and the File Tools and Folder
Tools split. The version stays at 2026.11.18.
See [Audio gain and key changes](user/audio-gain.md).

The final StandaloneTests Release build passed without warnings, and the
serial Windows test run below passed 888/888. The final key-change preview
was rendered at minimum desktop width. The two-version updater check is
the next open verification. Native Linux and macOS checks remain open.

## Change Audio Key

2026-11-23, `master` at `addb2d7`, version 2026.11.18. Uncommitted changes
add **File Tools → Change Audio Key** and `change-audio-key` for CLI and MCP.
The desktop uses the shared grouping-style preview. The FFmpeg wrapper shifts
pitch by whole semitones from −12 to +12 and retains the decoded track length
before encoding. The operation applies no gain or peak adjustment. It
publishes copies of loose audio and single-track karaoke ZIPs, keeps CDG
payloads intact, and saves the approved shift for recovery.
See [Change Audio Key](user/audio-gain.md#change-audio-key).

The StandaloneTests Release rebuild passed without warnings. With local
test-host access, `dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj
-c Release --no-build --no-restore -p:UsedAvaloniaProducts=
-p:ArtifactBuildName=StandaloneTests --blame-hang-timeout 25s
--blame-hang-dump-type none --results-directory artifacts/tests/results
-- xUnit.ParallelizeTestCollections=false` passed 888/888. Tests checked pitch,
decoded WAV length, cue timing, ZIP and loose CDG bytes, recovery, and that a
loud MP3 succeeds without the gain tool's ceiling. An isolated Avalonia render
checked the final preview at minimum desktop width. Native Linux and macOS
checks and the two-version updater check remain open.

## Audio gain for karaoke ZIPs

2026-11-23, `master` at `addb2d7`, version 2026.11.18. Uncommitted changes
let Desktop, CLI and MCP measure and adjust a single audio/CDG pair in a
karaoke ZIP. Core rebuilds and verifies the archive in the existing destination
transaction. CDG and extra entry payloads are preserved. See
[Normalize Audio Gain](user/audio-gain.md).

The StandaloneTests Release build passed without warnings. The local test-host
run with `--filter 'FullyQualifiedName~AudioGain|FullyQualifiedName~Tests.Archives'`
passed 100/100. The broader run hit a stalled MKV probe and truncated WebM
probe JSON in existing tagging tests. A separate
`--filter 'FullyQualifiedName~KaraokeTaggingTests' --blame-hang-timeout 25s`
rerun passed 25/25. The ZIP picker filter was covered by a desktop view-model
test. Live ZIP UI interaction and non-Windows runtime checks remain open.

## File Tools and Folder Tools

2026-11-23, `master` at `addb2d7`, version 2026.11.18. Uncommitted desktop
changes split the combined category. File Tools contains filename changes,
tagging, and audio gain. Folder Tools contains grouping and flattening. The
two categories inherit the legacy expansion preference until saved separately.
Operation IDs and favourites keep their existing keys.

The StandaloneTests Release rebuild passed without warnings. The serial
Windows `dotnet test` run with local test-host access and
`--filter 'FullyQualifiedName~Tests.App|FullyQualifiedName~OptInPersistsAndIsHiddenByDefault'`
passed 175/175. The category split was verified by desktop view-model tests.
The two-version updater check remains open.

## Normalize Audio Gain

2026-11-23, `master` at `addb2d7`, version 2026.11.18. Uncommitted
changes add a grouping-style desktop preview and the `normalize-audio-gain`
operation in CLI and MCP. Core uses the existing FFMpegCore wrapper for
integrated loudness and true-peak measurements and constant gain. It stages
adjusted MP3, AAC, ALAC, WAV, FLAC, WMA and Ogg Vorbis copies with any matching
CDG. The saved plan lets interrupted runs resume from verified copies.
See [Normalize Audio Gain](user/audio-gain.md).

The StandaloneTests Release build passed without warnings. With local test-host
access, `dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release
--no-build --no-restore -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests
-- xUnit.ParallelizeTestCollections=false` passed 877/877, including fixed gain
and true-peak output for eight format variants. An isolated Avalonia render
at the desktop minimum width showed two measured tracks and the Apply Gain
action. The desktop entry moved to File & Folder Tools. The Release rebuild
passed without warnings, and the same test command with
`--filter 'FullyQualifiedName~App.Metadata|FullyQualifiedName~AudioGainViewModelTests'`
passed 5/5. Native Linux and macOS runs remain unverified. Next, run the open
two-version updater check.

## Commit checkpoint

2026-11-23, `master`, version 2026.11.18. The recovery, hashing, desktop
progress and preview changes accumulated after `363c202` are recorded
together in the commit containing this checkpoint. The version stays at
2026.11.18. The entries below record the checks made during development.
The two-version updater check and visual inspection of the final desktop
previews remain open.

## Apply approved file previews

2026-11-23, `master` from `363c202`, version 2026.11.18. Uncommitted
changes apply approved Track ID, alphabetical, rename and tagging previews
without rediscovering inputs. Grouping and rename skip unavailable groups
before publication. A partially published group retains its recovery
checkpoint. Tagging verifies each approved source at the item. Recovery
discovery at Apply checks known folders without a recursive media scan.

The Track ID preview omits the destination field and Browse button. The
initial flow requests a destination when the selection has no single default.
Start requires existing, usable inputs, and the preview reports unrecognized
track names. Markup verified the removed controls.

The StandaloneTests Release build passed without warnings. The focused
`dotnet test` run passed 153/154 with an intermittent truncated FFprobe
response in the WebM tagging test. The broader regression run passed 324/324
including that test, Core file operations, recovery, CLI and MCP. After adding
the output path guard, the Track ID and operation-flow rerun passed 60/60.
Desktop rendering was not visually checked.

## Live file previews

2026-11-23, `master` from `363c202`, version 2026.11.18. Uncommitted desktop
changes for Track ID grouping, alphabetical grouping, filename renaming and
tagging reuse the scan when options change. Refresh reads the disk again.
Track ID grouping can show the proposed tree for an unscanned destination
and requires Refresh to check its conflicts. Counted preview progress shows
counts and a whole percentage.

The StandaloneTests Release build passed without warnings. The first corrected
focused run passed 129/130 and hit the previously observed truncated FFprobe
JSON in the WebM tagging test. The no-build rerun, including the workflow
recovery tests, passed 145/145. Desktop rendering was not visually checked.

## Group by Alphabet settings

2026-11-23, `master` from `363c202`, version 2026.11.18. The uncommitted
Settings markup removes the duplicate Ignore leading ‘The’ control. The Files
mode in the Group by Alphabet preview still has the checkbox. Markup checks
confirmed both locations. The StandaloneTests Release rebuild passed without
warnings, and the no-build Settings and alphabetical grouping tests passed 65/65.

## Recovery settings typography

2026-11-23, `master` from `363c202`, version 2026.11.18. An uncommitted
Settings markup change applies the shared `SmallTextSize` resource and help
opacity to all three Recovery descriptions. A markup check verified those
references. `dotnet build` for the StandaloneTests Release output passed with
zero warnings, and the filtered `dotnet test` run passed all 59 App Settings
and Metadata tests. Visual inspection of the running window is pending.

## Shared file staging

2026-11-23, `master` from `363c202`, version 2026.11.18. Uncommitted changes
share the streaming copy and fingerprint routine for conversion staging and
grouping copies. Conversion verifies staged bytes and rechecks original files
at publication. Move and copy workflows retain their recovery checkpoints.
See [recovery settings](user/crash-recovery.md#recovery-settings).

The local Windows benchmark used eight synthetic WAV/CDG pairs (128 MiB), one
warmup per build and operation, and five alternating measurements with
per-item xxHash128. Median first completed item / total were 411 / 1,485 ms
before and 413 / 1,513 ms after for Audio+G to ZIP, and 215 / 632 ms before
and 217 / 631 ms after for grouping copy. This cached-input fixture showed
no clear speedup. Raw measurements are in
`artifacts/benchmarks/verified-copy-e2c682f0/measurements.json`.

The StandaloneTests Release build passed without warnings. The no-build Windows
suite passed 849/852. One FFprobe child stalled on a disposable MKV fixture and
was stopped. The WMA GUI and WebM tagging cases also failed in that run.
Timeout-bounded `dotnet test` reruns with local test-host access passed 148/148
for recovery, grouping, overwrite review, conversions, maintenance and WMA,
and 24/24 for tagging. All three initially failing tests passed on rerun.
The two-version updater check remains open.

## Shared conversion fingerprints

2026-11-23, `master` from `363c202`, version 2026.11.18. Uncommitted
changes use the selected recovery verification mode for standalone conversion
baselines, staging, conflict review, and publication. The default is xxHash128.
Core content fingerprints use one handler. Archive CRCs and fixed SHA-256
contracts for quarantine, tagging, recipe recovery, and lock identities
retain their respective rules. Older conversion journals retain SHA-256.
An interrupted conversion publication without content fingerprints retains its
workspace for manual review. See [recovery settings](user/crash-recovery.md#recovery-settings).

The StandaloneTests Release build passed without warnings. The serial,
no-build Windows suite passed 849/850 with local test-host access. One
recipe-library save received Windows access denied. The targeted rerun
passed 649/649, including that recipe test and conversion mode, archive,
tagging, file operation, settings, CLI, MCP, and recovery coverage.
The two-version updater check remains open.

## Conversion file-check timing

2026-11-23, `master` from `363c202`, version 2026.11.18. Uncommitted
changes extend the shared timing setting to standalone media conversions.
Batch timing saves SHA-256 baselines for the selected input groups and their
audio companions before conversion starts. Staging compares each input to
its saved baseline, including during recovery. Per-item timing is the default.
Recipe-owned conversions keep their workflow checks. See
[recovery settings](user/crash-recovery.md#recovery-settings).

The StandaloneTests Release build passed with zero warnings. The no-build
`dotnet test` run with local test-host access and serial xUnit collections
passed 842/842 on Windows. Tests cover a waiting audio companion edited
across a multi-folder batch, conversion recovery against a saved baseline,
and CLI/MCP timing preferences. The two-version updater check remains open.

## File-change check timing

2026-11-23, `master` from `363c202`, version 2026.11.18. Uncommitted changes
add a shared timing choice for standalone file moves, renames, and grouping
copies. Item timing is the default for new runs without a saved preference.
It fingerprints each move group before publication and captures copy
fingerprints during staging. Recovery uses the saved timing and checkpoints.
Older journals default to batch timing. See
[recovery settings](user/crash-recovery.md#recovery-settings).

The StandaloneTests Release build passed with zero warnings. The no-build
`dotnet test` run with local test-host access and serial xUnit collections
passed 836/837 on Windows. A WMA test received truncated FFprobe JSON.
The targeted rerun passed 370/370, including that WMA test, file operations,
recovery, settings, CLI and MCP. The two-version updater check remains open.

## Recovery preferences

2026-11-23, `master` from `363c202`, version 2026.11.18. Uncommitted changes
expose Desktop startup discovery and batch continuation in the shared profile.
Both default on. Turning off continuation keeps recovery for the interrupted
item in standalone conversions, tagging, and verified file-move batches.
Recipes retain workflow continuation. Older non-resumable move journals and
moves without content fingerprints still use manual review. See
[recovery settings](user/crash-recovery.md#recovery-settings).

The StandaloneTests Release build passed without warnings. `dotnet test
KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-build --no-restore
-p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests --
xUnit.ParallelizeTestCollections=false` passed 819/819 with local test-host
access on retry after truncated FFprobe JSON in an MKV tagging test.
Tests cover shared settings and overrides, startup versus manual discovery,
and item-only recovery for a partial conversion, tagging run, and grouping
copy or move. Live Desktop inspection and the two-version updater check remain open.

## Activity elapsed time

2026-11-23, `master` from `363c202`, version 2026.11.18. Uncommitted desktop
changes give the shared processing screen a monotonic clock that refreshes
once a second without a progress event. The clock freezes when the run settles,
and each activity row keeps the elapsed time it was recorded at.

The StandaloneTests Release build passed with zero warnings. A no-build
`dotnet test` run with local test-host access passed 172/172 desktop and
track-grouping execution tests, including the clock regression test. Live GUI
timing was not checked. The settings and updater cutover remains open.

## Lossless Audio+G compatibility

2026-11-23, `master` from `363c202`, version 2026.11.18. Reverted the
uncommitted source-rate experiment. Lossless Audio+G to MP3+G uses 44.1 kHz
for compatibility. Encoder settings expose the format and bitrate. A user-set
output rate would require a new setting. See [audio runtime](distribution/audio-runtime.md#behavior-and-tests).

The StandaloneTests Release build passed with zero warnings. The no-build
`dotnet test` run with local test-host access passed 32/32 tests filtered to
`LosslessAudioGTests`, `FFmpegAudioTests`, and `AudioConversionContractTests`.
The settings and updater cutover with two installed Windows versions remains open.

## Track IDs with letter suffixes

2026-11-23, `master` from `363c202`, version 2026.11.18. Uncommitted changes
let grouping use the numeric part of IDs such as `ABC-369A` while retaining
the full filenames and audio/CDG pairing. Bare start-only folder labels still
require an explicit interval. The numeric Track tag still requires digits.
The [whole-ID tagging preset](user/tagging.md) keeps the suffix in Album.
See [Group by Track ID](user/track-grouping.md).

`scripts/Test.ps1 -NoRestore -Filter` built the StandaloneTests Release output,
then VSTest timed out connecting inside the sandbox. A `dotnet test` rerun
with `--no-build --no-restore` and local test-host access passed 65/65 tests
filtered to track grouping, filename patterns and maintenance contracts.
A CLI `run group-by-track-id ... --dry-run` with a suffixed audio/CDG pair
proposed both original names under the same masked folder. The settings and
updater cutover with two installed Windows versions remains open.

## Filename and file workflows

2026-11-23, `master` from `ba73400`, version 2026.11.18. This change includes
shared filename presets, pattern-based renaming, batch conflict review,
alphabetical grouping and shared command names. The updated guides cover
these workflows and the settings reset. Verified 228 local Markdown links
across 44 files and checked whitespace with `git diff --check`. The dated
entries below record the individual implementation and test checkpoints.
The next runtime check is the settings and updater cutover with two
installed Windows versions.

## Shared CLI and MCP operation names

2026-11-23, `master` from `ba73400`, version 2026.11.18. Uncommitted changes
make CLI and MCP invocation names follow the same desktop terminology.
Both alphabetical scopes use `group-by-alphabet-*` names. CLI help,
completion and desktop integration examples use the preferred commands.
MCP discovery exposes `name` for invocation and `coreId` for recipe steps.
Retired public spellings have no aliases. Recipes and recovery use the
Core operation identities internally.

The StandaloneTests Release build with SDK 10.0.404 passed without warnings.
The no-build suite passed 807/807 with local test-host access and xUnit
collections run serially. Tests include CLI parsing, MCP discovery and calls
over stdio, and rejection of retired names. The built CLI also listed and
showed help for the new alphabetical commands.
See [CLI](user/cli.md) and [MCP](user/mcp.md).

## Group by Alphabet

2026-11-22, `master` from `ba73400`, version 2026.11.18. Uncommitted
changes combine the desktop alphabetical entries in the shared preview
workspace. The Files and Folders scopes keep their existing move behavior.
The preview keeps blocked groups together, and Core rechecks the approved
destinations before saving the move plan. Recipes, headless commands and
journals keep both operation identities.

The StandaloneTests Release build with SDK 10.0.404 passed without warnings.
The no-build suite passed 803/803 with local test-host access and xUnit test
collections run serially. Two parallel runs each passed 802/803 and hit
distinct FFprobe JSON failures. A live Windows preview with disposable
fixtures confirmed Files, Folders and the skipped pane. See
[file workflows](user/file-workflow-features.md#group-files-and-folders).

The settings reset retires the old folder-favourite mapping. The targeted
alphabetical, favourites, catalogue and MCP tests passed 52/52 with
local test-host access.

## Batch review for existing conversion outputs

2026-11-22, `master` from `ba73400`, version 2026.11.18. Uncommitted
App and Core changes gather conflicting groups across a standalone desktop
conversion using Ask each time. Nonconflicting media publishes as it finishes.
The final review offers bulk and individual choices, including automatic
CUE/TXT conflicts. Verified media waits in disposable staging and approved
replacements recheck sources, destinations and staged payloads. Resume can
regenerate unpublished results and review the remaining conflicts.

The StandaloneTests Release build with SDK 10.0.404 passed without warnings.
The no-build suite passed 800/800 with local test-host access, including
multi-folder review, mixed choices, cancellation, changed destinations and
interrupted review recovery. A live Windows dialog with 105 synthetic rows
confirmed Skip all, Replace all and a per-group override. See
[file workflows](user/file-workflow-features.md#review-existing-conversion-outputs).

## Shared operation preview layout

2026-11-22, `master` from `ba73400`, version 2026.11.18. Uncommitted
changes move track grouping and filename renaming onto a shared desktop preview
layout. Rename shows filename pairs separately from skipped items and conflicts.
The pattern editors vertically center their text. Uppercase Disc ID defaults
on and supports case-only paired renames with recovery.

The StandaloneTests Release build with SDK 10.0.404 succeeded with zero
warnings. The no-build suite passed 794/794 with local test-host access using
the properties below. In live Windows preview windows with disposable
fixtures, verified both layouts, the issues divider, centered pattern text,
and the Disc toggle clearing the stale preview. The settings cutover with two
installed versions remains open.

The follow-up adds 12 DIP between the shared preview and footer and shortens
the rename guidance to the tokens and two brief instructions. The App Release
build with SDK 10.0.404 passed without warnings. A live rename preview with
both a conflict and a skipped item confirmed the footer gap and two-line help.

The preview splitter can collapse the issues row fully. Its heading and list
hide together when the row becomes too short to read, and dragging the handle
back up restores both. The App Release build passed without warnings, and a
950 × 540 Windows preview confirmed collapsing and reopening the pane.

Remove spaces from Disc ID defaults on in the rename preview and Core
request. The StandaloneTests Release build with SDK 10.0.404 passed without
warnings. The no-build `FullyQualifiedName~FilenameRename` test run passed
10/10 with local test-host access, including the option turned off.

## Rename by Filename Pattern

2026-11-22, `master` from `ba73400`, version 2026.11.18. Uncommitted
Desktop and Core changes add a preview-driven rename using a source pattern,
a destination pattern and an optional Disc ID whitespace removal. Renames
preserve extensions and audio/CDG pairing, block collisions, and use the
existing file-move recovery journal. New runs start in Desktop.
See [file workflows](user/file-workflow-features.md#rename-by-filename-pattern).

With SDK 10.0.404, the StandaloneTests Release build using `--no-restore -m:1`
and the properties below succeeded with zero warnings. The no-build suite
passed 790/790 with local test-host access, including desktop routing, stale
plans, late conflicts and interrupted pair recovery. See the current entry
above for visual verification of the updated layout.

## Legacy online-renaming references in Settings

2026-11-22, `master` from `ba73400`, version 2026.11.18. Uncommitted App
changes remove the unavailable online-renaming card from Settings → General
and its unused display metadata. Saved configuration retains compatibility.
The existing uncommitted filename parser and preset work is recorded below.

The StandaloneTests Release build passed with SDK 10.0.404 using the command
and properties below. A no-build rerun filtered to App Settings, App Metadata,
GuiWorkflowTests and Core Settings passed 92/92 with local test-host access.
The Settings markup was checked for remaining references. A live window
inspection was not performed. The settings cutover verification remains open.

## Shared karaoke filename parsing

2026-11-22, `master` from `ba73400`, version 2026.11.18. Uncommitted
Core, App, test and documentation changes add a shared filename template
parser. The desktop tagging dropdown lists seven built-in layouts ahead of
custom history and opens on the last accepted pattern. Presets cover a whole
disc identifier, a split disc and track at either end, and artist/title
without an ID. Tagging maps parsed track digits to a numeric tag. Grouping and
separator edits reuse leading ID recognition with their existing separator rules.
See [Core structure](development/core-structure.md) and [tagging](user/tagging.md).

With SDK 10.0.404, `scripts/Test.ps1 -NoRestore` built successfully, then
VSTest timed out connecting to the sandboxed test host.
`dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-build --no-restore -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests`
passed 776/776 with local test-host access. After wiring the dropdown, the same
Release test project built with SDK 10.0.404 using `--no-restore -m:1` and the
same build properties. The no-build test rerun passed 777/777 with local
test-host access after an initial MKV test received truncated FFprobe JSON.
After expanding the presets, the same Release build succeeded with zero
warnings and the no-build test run passed 784/784 with local test-host access.
Tests cover the new layouts, dropdown population, and both mappings of a
manufacturer-prefixed track ID. The dropdown was checked by automated tests
and its compiled binding. Whitespace checks passed.
The next outstanding verification is the settings cutover with two installed
versions.

## Shared processing profile and migrations

2026-11-22 checkpoint, `master` based on `959239a`, version 2026.11.18.
Desktop, CLI and MCP use versioned `settings.xml` with a shared typed
processing catalogue and App-owned desktop preferences. The preview starts
from current defaults, including xxHash128 for file moves. The bundled
`deleteAppDataFile` migration removes `options.xmls` at the known old paths after
the new profile is saved. Core records completion after deletion and retries
pending cleanup on startup or an authorized settings write. Headless reads
leave both files alone. Recipes and recovery journals retain their own settings.
See [settings format and migrations](development/settings-format.md).

`scripts/Test.ps1 -NoRestore` passed 772/772 with local test-host access on
rerun after an old-format test fixture was updated. The sandboxed test host
timed out and the first local run also had an isolated FFprobe failure.
Whitespace checks passed. Tests exercised defaults, one-time deletion, failed
save, cleanup retry, typed round-trips and CLI/MCP integration. Next, verify the
cutover with two installed versions.

## Navigation during an operation

2026-11-21, `master` from `959239a`, version 2026.11.18. Uncommitted desktop
changes use a shared busy-navigation guard for main-window Back and Exit
and for closing disc ripping or the results window. The operation runs
while the prompt is open. Confirming uses existing cancellation where
available and waits for the operation to settle before leaving.

The Release build and full suite commands in Desktop dialog design passed
761/761. On Windows, a disposable validation run progressed to completion
behind the Back prompt. Keeping the window open retained the report, and
confirming Exit after a second run closed the window. Unit tests cover
cancellation and waiting. Disc and results prompts were not driven live.

## Default file-move verification

2026-11-21, `master` from `959239a`, version 2026.11.18. Uncommitted changes
default new standalone track grouping, alphabetical organization and flatten
runs to xxHash128 in Desktop, CLI, MCP and direct Core calls. Saved choices and
journals retain their verification mode. Legacy journals without a mode and
recipe workflows use SHA-256.

A local 500-file, 5 GB grouping benchmark took 17.7–19.9 seconds with SHA-256
and 5.7 seconds with xxHash128. The Release build and full suite commands in
Desktop dialog design passed 757/757 with local test-host access. Tests cover
the defaults, explicit SHA-256 and a legacy journal. The Desktop default
was checked through settings tests. Updater verification still needs two
installed versions.

## Scan, resume and quarantine progress

2026-11-21, `master` from `959239a`, version 2026.11.18. Uncommitted changes
reuse the small footer bar for startup and manual recovery discovery, initial
validation and conversion scans, resume verification, and quarantine moves
and restores. File-move resume performs its lookup off the UI thread.

The Release build and full suite commands in Desktop dialog design succeeded.
The suite passed 754/754 on retry after a settings file replacement failure.
Tests cover the footer bar showing and hiding during validation and recovery
index checks. These new paths were not checked visually.

## Grouping plan and recovery progress

2026-11-21, `master` from `959239a`, version 2026.11.18. Uncommitted changes
keep the small footer bar visible during both grouping plan revalidation and
recovery preparation. The grouping regression test checks both progress events
and the return to determinate progress.

The Release build and full suite commands in Desktop dialog design succeeded
with 753/753 tests. Live verification of the grouping transition remains
open because the Windows automation helper could not launch a second instance.
The earlier flatten UI check is recorded below.

## Recovery preparation progress

2026-11-21, `master` from `959239a`, version 2026.11.18. Uncommitted changes
show an animated desktop footer bar while the file organization, flattening
and track grouping engines prepare recovery. The shared progress event marks
the preparation phase. File verification and checkpoint writes retain their
existing behavior.

The Release build and full suite commands in Desktop dialog design succeeded
with 753/753 tests. Visually checked the bar during a Windows flatten run
and confirmed it cleared on completion. A separate CLI alphabetical file
grouping run with SHA-256 on 350 synthetic audio/CDG pairs (1.2 GiB) took 3.4 seconds from the
preparation message to the first processing event and 8.1 seconds total.
The fixture was removed. Next, verify the updater with two installed versions.

## Dialog files

2026-11-21, `master` from `959239a`, version 2026.11.18. Uncommitted App and
documentation changes move ten code-built prompts into named files in their
feature's `Dialogs/` folder. Each prompt supplies its own actions through
the shared footer. See [App structure](development/app-structure.md).

The Release build and full suite commands below succeeded again after the
move. The suite passed 752/752 with local test-host access. Live dialog
checks were from before the move. Next, verify the updater with two
installed versions.

## Desktop dialog design

2026-11-21, `master` from `959239a`, version 2026.11.18. Uncommitted App and
documentation changes apply the [desktop UI guidelines](development/ui-definitions.md)
to short prompts and larger modal windows. Short prompts share their heading,
warning artwork, footer and button spacing.

`dotnet build KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -m:1 -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests`
succeeded. `dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-build --no-restore -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests`
passed 752/752 with local test-host access on retry after an FFmpeg OGG encoding
failure. Checked Settings, Reset to Defaults, Help and Recipe Builder visually
on Windows, including Escape dismissal of the reset prompt. Other dialogs
were checked in source and by the build. Restored the About catchphrase and
verified the App Release build again. Next, verify the updater scenarios below
with two installed test versions.

## In-app updates

2026-11-18, `master` from `a818a98`, version 2026.11.18. The updater adds
**Update** beside **Close** and **Restart to update** after downloading.
The published 2026.11.18 prerelease has the earlier check-only dialog.

`scripts/Test.ps1 -NoRestore` passed 752/752 with local test-host access on
retry after a concurrent settings-writer failure. Next, verify download,
cancellation, restart and data preservation with two installed test versions.

## 2026.11.18 Windows x64 prerelease

2026-11-18, `master` from base `5662a02`. See the
[2026.11.18 changelog](changelogs/2026.11.18.md) for user-visible changes and
[publishing](distribution/publishing.md) for Windows packaging and release assets.
Release scope includes the desktop recovery and flattening fixes, in-window
previews, dialog and menu updates, and Track ID separator operations.

`scripts/Test.ps1 -NoRestore` passed 752/752 with local test-host access.
Temporary dialog harness builds were removed. Review PNGs, runtime caches,
and release packages remain in ignored artifacts.

Physical CD+G capture and clean-machine Windows Setup prerequisite installation
remain unverified. Linux and macOS runtime verification is pending.
See [TODO](todo.md) for the backlog.
