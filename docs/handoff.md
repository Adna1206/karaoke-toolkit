# Current handoff

Snapshot: 2026-11-04, Africa/Johannesburg. Verify Git and files before editing;
this document is a dated status record, not a substitute for current state.

## Checkout and version

- Branch at preparation: `master`, tracking `origin/master`.
- Latest commit at preparation: `a97db6c` — documentation plans for macOS packaging
  and GitHub CI/release automation. Pushed successfully.
- App version: **2026.11.2**, defined in `Directory.Build.props`.
- Checkout was clean before the documentation work. The current change set adds
  `AGENTS.md`, this handoff and a docs index, normalizes guide filenames and corrects
  stale/duplicated documentation. Use `git status --short` for pending changes;
  the user requested committing and pushing the finished documentation cleanup.

## Completed work

- Added agent guidance and this handoff. The user authorized routine clarification,
  extension and cleanup of `AGENTS.md`; major policy, permission, architecture or
  workflow changes require checking with the user first. See its maintenance section.
- Reviewed repository Markdown, added `docs/README.md` for navigation, lowercased
  `todo.md`, `handoff.md` and the vendor inventory, and corrected UI/provider/runtime
  notes. Feature docs describe current behaviour; README identifies the application's
  inspiration. Completed recipe items are covered in feature guides rather than TODO.
- Documentation changes were checked for local links, filename case, stale names
  and whitespace; the runtime suite was not repeated for this documentation-only task.
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
- Agreed future work is in [TODO](todo.md), including CLI/MCP, a setup page,
  MSI packaging, visible Velopack credit and GitHub automation. These are not
  implemented simply because they appear in that list.

## Verification evidence

Last full suite at version 2026.11.2: **572 passed, 0 failed** on Windows.

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests -v minimal --logger 'trx;LogFileName=release-2026.11.2.trx' --results-directory artifacts/tests/results
.\scripts\Publish.ps1
```

Both publish targets completed successfully. Package checksums and the Windows
managed app's version (`2026.11.2`, file version `2026.11.2.0`) were verified.
Local outputs, if still present:

- `artifacts/releases/2026.11.2/20261104-175642-13ee68/win-x64/KaraokeToolkit-win-Setup.exe`
- Corresponding Windows portable ZIP and Linux AppImage under the same run folder.
- Test report: `artifacts/tests/results/release-2026.11.2.trx`.

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
- There is no release hosting, enabled update flow, CLI, MCP executable, modern
  Windows 11 shell extension or MSI installer yet.
- Playback is deferred for discussion. Do not add it as part of unrelated work.
- No next feature has been selected. This task covers documentation cleanup and
  its commit/push; no application code or version changes are needed. Use TODO when the
  user chooses subsequent work rather than starting the backlog unaided.

For lasting instructions and build commands, see [AGENTS.md](../AGENTS.md). For
feature details, follow the links there and in README. Refresh this snapshot when
work reaches another meaningful handoff point.
