# Current handoff

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
