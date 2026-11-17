# Current handoff

## Operation previews and batch feedback

2026-11-17, `master` from base `f498bf3`, version 2026.11.17. Tagging and
Track Grouping use the main workspace. Back keeps the input selection and
confirms before discarding edits. The destination tree supports bulk expansion
and a bounded issues divider. Update and Help use the shared footer.

Recipe Builder combines the name field and saved recipe menu, gives new
recipes instructive placeholders, and uses the shared Delete Recipe footer.
Flattening reports failed empty-folder cleanup and continues. Desktop
completion is red for recorded failures. The batch rule is in AGENTS.md and
the crash recovery guide. TODO records favourites ordering, stem extraction,
and undo for the last move or rename.

`scripts/Test.ps1 -NoRestore` passed 749/749 with local test-host access.
Next, visually check the in-window previews, divider limits and recipe menu.
The original OneDrive access-denied case was not reproduced.

## Desktop status and tagging controls

2026-11-17, `master` from base `2755ffe`, version 2026.11.17. This batch
standardizes desktop footer spacing, dividers and typography, and uses one
shared status slot per window with progress to the right where applicable.
Tagging now has an editable pattern dropdown with centered text, Refresh,
bulk row selection, and an accent button when tags can be applied.
The update popup uses plain text beside its progress bar, then shows the
result in the same popup.

`scripts/Test.ps1 -NoRestore` passed 748/748 with local test-host access
before the popup restyle. The Release App build passed after the restyle.
Next, visually check the pattern field, status transitions and footer spacing.

## Desktop and operation changes

2026-11-17, `master` from base `f27cf0f`, version 2026.11.17. This batch adds
startup and operation wait indicators, the resizable Track Grouping issues panel,
and Add/Remove Track ID Separator across desktop, CLI, MCP and recipes. FFprobe
retries once on malformed JSON. Explorer multi-selection is documented in
[TODO](todo.md).

`scripts/Test.ps1 -NoRestore` passed 748/748 with local test-host access on the
final working tree. The batch is committed. Next, visually check desktop startup,
the preview divider and progress indicators, and smoke-test disc detection on a
physical drive. The cause of intermittent FFprobe truncation remains unverified.

## Private vulnerability reporting

Enabled on GitHub on 2026-11-16 from `master` at `b3314ac`. GitHub's API
confirmed the setting and the public Advisories page showed the report button.
The [security policy](SECURITY.md) links to the private form.

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
