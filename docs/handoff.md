# Current handoff

Snapshot: 2026-11-04, Africa/Johannesburg. Verify Git and files before editing;
this document is a dated status record, not a substitute for current state.

## Checkout and version

- Branch at preparation: `master`, tracking `origin/master`.
- Latest commit before CLI work: `112547b` — documentation cleanup and agent guidance,
  pushed successfully.
- App version: **2026.11.3**, defined in `Directory.Build.props`.
- Checkout was clean before CLI work. The current uncommitted change set adds
  `KaraokeToolkit.Cli`, shared Core operation identities/execution contracts, tests
  and publishing/docs updates. The user has now requested a version bump/commit before extending all remaining CLI features; push is not requested.
  Use `git status --short` for the current state.

## Completed work

- Created the GUI-independent CLI with 20 individual operation capabilities,
  per-run settings, help/discovery, JSON/text reports, Ctrl+C cancellation, exit
  codes, tagging previews and conversion/tagging recovery. No recipe execution or
  physical disc capture. Core's `Execution` services are reusable by future MCP.
- Centralized 24 distinct built-in identities in `Execution/OperationId`, with
  PascalCase names and no reserved CDG modes or duplicate cleanup aliases.
  General invocation is `Execution/OperationRequest`; engine-specific invocation
  is `Conversion/ConversionRequest`. App, CLI and recipe registrations share these IDs.
- Operation JSON always uses names, including nested retries; numeric identities
  are rejected. Run/workflow journals use version 2 and named enum values for
  statuses/policies as well. Reports already use readable names. Per user direction,
  there is no migration for old pre-production journals or underscored IDs.
- CLI now explicitly includes/excludes every enum entry with no generated fallback.
  Core and CLI treat CS8509 as a build error; isolated source copies with an extra
  enum entry confirmed both catalogues reject an unaccounted-for operation.
- Reviewed CLI command/help/settings consistency. Text discovery now shows Core IDs
  and folder-only requirements; help documents defaults, video bitrate matching,
  scope rules and examples. Video/leading-The flags reject unrelated operations;
  options JSON rejects numeric enums except explicit ZIP levels. Added
  [CLI examples](cli-examples.md) and documented remaining CLI boundaries.
- Physical capture is the single `DiscRip` operation with `RipFormat`. It stages
  track BINs and calls shared processing components for Audio+G/ZIP. Hardware
  capture remains unverified; ordinary file converters are supported.
- Publish script now combines App and CLI in Windows/Linux packages. CLI also
  supports standalone publishing. See [CLI](cli.md) for usage and scope.
- Added agent guidance and this handoff. The user authorized routine clarification,
  extension and cleanup of `AGENTS.md`; major policy, permission, architecture or
  workflow changes require checking with the user first. See its maintenance section.
- Reviewed repository Markdown, added `docs/README.md` for navigation, lowercased
  `todo.md`, `handoff.md` and the vendor inventory, and corrected UI/provider/runtime
  notes. Feature docs describe current behaviour; README identifies the application's
  inspiration. Completed recipe items are covered in feature guides rather than TODO.
- Shared test suite renamed to `KaraokeToolkit.Tests`, including folder/project,
  namespaces, solution entries, friend assemblies, build targets and documentation.
- Core includes conversion/archive/audio/video/file tools, tagging and recipes.
  Recipe tagging saves its own pattern/options. Full recipe and tagging crash
  continuation use saved snapshots, transaction receipts and frozen move plans.
- Velopack 1.2.161 is integrated in App, with licence text and About notice.
  Update checks/feed and automatic application of pending updates are disabled.
- `scripts/Publish.ps1` publishes and packages self-contained Windows/Linux x64
  builds using pinned local `vpk`. Windows gets a one-click Setup EXE and portable
  ZIP; Linux gets an AppImage. Uploads and signing are not implemented.
- Agreed future work is in [TODO](todo.md), including CLI recipes/MCP, a setup page,
  MSI packaging, visible Velopack credit and GitHub automation. These are not
  implemented simply because they appear in that list.

## Verification evidence

Latest full suite after identity cleanup, at version 2026.11.2:
**609 passed, 0 failed** on Windows. Coverage includes CLI processes, settings,
conversion/tagging/recovery adapters, named operation serialization, rejection of
numeric journals, named nested workflow statuses/policies/retries, and App/recipe
identity agreement. Earlier numeric-value pinning and reserved-ID tests were removed.
Physical hardware testing is still outstanding.

After the CLI-only consistency review, **49 focused CLI/operation tests passed**:

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests -v minimal --filter 'FullyQualifiedName~KaraokeToolkit.Tests.Cli|FullyQualifiedName~KaraokeToolkit.Tests.Execution' --logger 'trx;LogFileName=cli-review.trx' --results-directory artifacts/tests/results
```

Version 2026.11.3 checkpoint: full suite **617 passed, 0 failed** (`cli-checkpoint.trx`).

Earlier full-suite/package commands:

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests -v minimal --logger 'trx;LogFileName=operation-catalog-refactor.trx' --results-directory artifacts/tests/results
.\scripts\Publish.ps1
```

Before the catalogue refactor, combined App/CLI publish targets completed successfully. The published Windows
CLI's version/discovery commands and portable payload entries were verified.
Local outputs, if still present:

- `artifacts/releases/2026.11.2/20261104-192842-d6ed7d/win-x64/KaraokeToolkit-win-Setup.exe`
- Corresponding Windows portable ZIP and Linux AppImage under the same run folder.
- Test report: `artifacts/tests/results/operation-catalog-refactor.trx`.

These CLI verification packages predate the catalogue refactor and reuse the current
version; rebuild and bump before distributing another update. The earlier
`20261104-175642-13ee68` packages predate the CLI.

Artifacts are ignored, machine-local and regenerable. Earlier cleanup reclaimed
about 15.58 GiB; builds/tests/publish can recreate staging and fixtures. Retained
installers, runtime cache, benchmark results and reports were intentionally kept.

## Open limitations and next task

- Package generation is verified; installation/upgrade/uninstall behaviour has not
  been verified in a clean environment. The user was given the new Setup EXE to
  perform a manual update; no confirmation of its outcome has been received.
- Linux runtime execution, macOS builds and physical CD+G ripping remain unverified.
  Linux/macOS cdrdao is external. Native dependency/source obligations remain in
  [the licence inventory](../licenses/README.md).
- There is no release hosting, enabled update flow, MCP executable, modern
  Windows 11 shell extension or MSI installer yet.
- Playback is deferred for discussion. Do not add it as part of unrelated work.
- Current authorized task completed: review CLI command/help/settings consistency
  and add common-task examples. No packaging, version bump, commit or push was
  requested for this follow-up. CLI recipes remain explicitly deferred; current
  gaps are documented in the CLI guide. No next feature has been selected.

For lasting instructions and build commands, see [AGENTS.md](../AGENTS.md). For
feature details, follow the links there and in README. Refresh this snapshot when
work reaches another meaningful handoff point.
