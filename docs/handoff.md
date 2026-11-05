# Current handoff

Snapshot: 2026-11-05, Africa/Johannesburg. Verify Git/files before editing.

## Checkout and release

- Branch `master`, current base `060c5d1`; shared version **2026.11.10**.
  Cleanup code/tests and documentation below are included in the current commit,
  with commit/push requested. No version bump requested for this follow-up.
  No package publish or installation requested.

## Workspace cleanup and naming backlog

- Follow-up on `master` / `060c5d1`, version 2026.11.10, initially clean.
  User asked why a `.karaoke-job-*` folder remains and requested an output-naming TODO.
- Read-only inspection of the supplied OneDrive example found `backups/0`
  (4,474,905 bytes), read-only/cloud reparse attributes, and no recovery/run/workflow
  journal. No user files were modified or deleted.
- Reproduced the exact failure with disposable ignored artifacts: Windows
  `Directory.Delete(workspace, true)` removed recovery.json, then threw
  UnauthorizedAccessException on the read-only backup, leaving it behind.
  Previous disposal suppressed the exception; startup does not sweep these orphans.
- Implemented shared transaction/completed-checkpoint cleanup: preflight links,
  clear only disposable read-only attributes, bounded retries, delete journal last,
  attempt restoration if final directory deletion fails, and report persistent errors.
  Pending staging/publication cleanup retains its resumable owner checkpoint.
  Committed recovery accepts missing cleaned backups but verifies published outputs.
- Portable .NET deletion/retries; only Windows cloud-tag inspection uses guarded
  native metadata APIs. No product shell utilities. Linux/macOS runtime unverified.
  Historical orphans remain untouched; failed finished-checkpoint cleanup reports
  its path but does not appear as an unfinished job. See crash-recovery documentation.
- Full testing exposed transient shared recovery-index reader locks and silent
  workflow-folder cleanup failure; index deletion and checkpoint cleanup now retry.
- Added requested configurable generated-output names/templates TODO (TXT/CUE,
  defaults, validation, conflicts and recovery). No unrelated backlog implemented.
- Current verification: recovery/recipe/tagging subset **117 passed**; complete
  Release suite **691 passed, 0 failed, 0 skipped**, using command below with
  `ArtifactBuildName=Cleanup`. TRX:
  `artifacts/tests/results/almei_ANDRE_2026-11-05_12_48_22_net10.0.trx`.
  Coverage includes read-only staging/backups, blocked cleanup/resume, partial
  committed cleanup, links/cloud tags and transient/persistent index locks.
- Updated AGENTS: Windows remains the primary validated target; prefer portable
  APIs, guard OS-specific code and distinguish compatibility goals from validation.
- Next: package/installation and actual
  OneDrive cleanup execution have not been verified. Supplied folder is unchanged.
- Existing installers remain 2026.11.6. The publish script appends the evaluated app
  version to Windows Setup EXE names before generating checksums.
- Application licence: GPL-3.0-or-later, copyright © 2026 André Almeida (ADNA).
  Runtime licence copies and About reads use `licenses/`; native notice names use
  `Component.Document.txt`. See [distribution licensing](distribution-licensing.md).

## Completed maintenance

- Traced the flaky MCP progress-order assertion to pinned SDK 2.3.1 client dispatch:
  message handlers yield and execute concurrently, so callback arrival order is not
  wire order. SDK source was read at package commit
  `61420937cbef3eab895cafc8a89418811fb71f54` (ignored investigation files under
  `artifacts/checks/mcp-progress/`). No MCP production code or SDK version changed.
- Kept the SDK-client structured-report/original-retention test and adapter
  monotonic-progress coverage. Added a bounded raw-stdio protocol test that reads
  progress in actual stdout order, checks the token, monotonicity and final 100
  before the tool response, and reaps its child process. Removed only the invalid
  SDK-callback-order assertion.
- Expanded FileTransactionTests into readable setup/action/assertion blocks with
  descriptive paths. Original scenarios and assertions preserved; Core unchanged.
- Added App `ProcessingRun` to own cancellation, synchronization-context progress,
  progress shutdown and disposal. MainViewModel begin/end methods reset common
  screen state and release busy/recovery state. Conversion/retry/recovery,
  validation, file operations, tagging and recipes use it. Reports, outcome text,
  retry scope and recovery discovery remain workflow-specific.
- Added deterministic queued-progress tests, cancellation-followed-by-next-run and
  retry-scope coverage, plus cancelled/failed tagging-dialog cleanup checks. Modal
  disc ripping/restoration and tagging configuration retain their separate busy
  handling. Navigation/settings/favourites were not refactored.
- Updated [App structure](app-structure.md) and [test structure](test-structure.md).

## Verification

Full Release suite outside the sandbox: **677 passed, 0 failed, 0 skipped**.

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -p:UsedAvaloniaProducts= -p:ArtifactBuildName=Maintenance -v minimal --logger trx --results-directory artifacts/tests/results
```

- Post-bump run initially passed 676/677: `StdioRecipeUsesSavedStepsAndSettings`
  returned a tool error without diagnostic text in its assertion. Added the MCP
  text content to that assertion; the isolated rerun passed, then the entire
  built suite passed **677/677**. Underlying cause of the transient recipe error
  was not captured; no MCP runtime behaviour was changed to suppress it.
- Final post-bump rerun used the command above with `--no-build`; TRX:
  `artifacts/tests/results/almei_ANDRE_2026-11-05_12_23_34_net10.0.trx`.
  CLI and MCP `--version` both reported **2026.11.10**. Fetched origin; master
  had no remote commits missing locally before this release commit/push.
- Staged checks passed: MCP 19; recovery + new lifetime tests 44; conversion/main
  subsets 19 then 46; recipe/tagging/recovery 56; final boundary subset 17.
- Diff checks passed. No visual GUI, package, install/upgrade, Linux runtime or
  physical optical-drive verification. Tests verify Windows runtime behaviour.

## Open work and preserved decisions

- Recover exact FFmpeg external-library sources/notices/build inputs or choose a
  source-complete replacement with media regression checks. Recorded BtbN release
  and recipe tag returned 404. Public source access remains unconfigured; local
  source evidence is not full distribution clearance. Full newlib notices remain
  conservatively retained without proving applicability of every section.
- Fresh-checkout CI LFS setup remains a review concern. The prior MCP test failure
  is resolved by checking the correct ordering boundary, as recorded above.
- Settings Escape/central licence layout are in the committed baseline. Broader
  Settings redesign stays deferred. Update feed/checks and pending update application
  stay disabled. No player. Linux is cross-built; macOS packaging and physical disc
  validation remain outstanding. See [AGENTS](../AGENTS.md), [TODO](todo.md),
  [publishing](publishing.md) and the [licence inventory](../licenses/README.md).
