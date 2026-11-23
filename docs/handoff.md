# Current handoff

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
