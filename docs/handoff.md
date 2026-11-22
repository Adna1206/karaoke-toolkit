# Current handoff

## Shared processing profile and migrations

2026-11-22 checkpoint, `master` based on `959239a`, version 2026.11.18.
Desktop, CLI and MCP now use versioned `settings.xml` with a shared typed
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
and confirmed it cleared on completion. A separate
`karaoke-toolkit run organize-files <fixture> --recovery-verification sha256`
run on 350 synthetic audio/CDG pairs (1.2 GiB) took 3.4 seconds from the
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
