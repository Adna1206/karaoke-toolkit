# Current handoff

Snapshot: 2026-11-05, Africa/Johannesburg. Verify Git/files before editing.

## Checkout and release

- Branch `master`; shared version **2026.11.9**, bumped from 2026.11.8 on base
  `6c88cf0`. Current request: bump and commit the pending notice/About/Settings
  changes. No push, package publish or installation requested. Check Git for the
  resulting commit and working-tree state.
- Latest generated installers remain 2026.11.6; source/build version changes do
  not regenerate them. Windows installer naming now appends the evaluated app
  version before `.exe`; checksums use the final name.
- Application licence: **GPL-3.0-or-later**. Copyright © 2026 André Almeida (ADNA).
  Root LICENSE/NOTICE.txt remain repository entry points; third-party terms remain
  independent. See [distribution licensing](distribution-licensing.md).

## Completed in this batch

- All runtime licence copies and About reads use output `licenses/`: application
  texts under `application/`, common notices at the root, native notices under
  `ffmpeg/` and `cdrdao/`. Publish mappings and required-file checks cover them.
  Original upstream FFmpeg distribution notices also remain intact.
- Maintained cdrdao texts moved into repository `licenses/cdrdao/` and named
  `Component.Document.txt`. FFmpeg preparation generates the same naming scheme.
  Native display labels derive from origin/folder and filename, without a native
  filename switch or `ToUpperInvariant()`. Renamed legal texts are byte-identical.
- About omits audit/source manifests, the inventory README and common duplicate
  native licence entries from the displayed list. Supporting documentation still
  ships. About wording simplified; application licence remains on its own tab.
- Settings Cancel has `IsCancel=True`, giving Escape the existing cancel/close
  behaviour. Applied preferences stay saved; pending edits are discarded.
- AGENTS now explicitly requires the central runtime licence tree, canonical native
  notice names and keeping audit metadata out of the licence list.

## Verification

Latest Release suite, run outside the sandbox:

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -p:UsedAvaloniaProducts= -p:ArtifactBuildName=LicenseLayoutCheck -v minimal --logger trx --results-directory artifacts/tests/results
```

- **669 passed, 1 failed, 670 total**. Failure:
  `McpTests.StdioProcessingKeepsOriginalsAndReturnsStructuredReport` at line 119:
  progress arrived `[0, 0, 100, 0]` instead of monotonically. Same failure occurred
  in the prior full run and passed its earlier isolated rerun. No MCP code changed
  in this batch; this is an unresolved issue, not a clean full-suite result.
- Earlier targeted About suite **3/3** and Settings suites **15/15** passed.
  Fresh App/CLI/MCP output hashes and Build + `ComputeFilesToPublish` metadata
  verified licence copying/mappings. All six renamed cdrdao texts matched their
  previous prepared copies byte-for-byte. PowerShell parsing/diff checks passed;
  upstream verbatim notice whitespace retained.
- No visual GUI, interactive Escape, full package, install/upgrade, Linux runtime
  or physical optical-drive verification. Reports remain under ignored artifacts.

## Open work and preserved decisions

- Recover exact FFmpeg external-library sources/notices/build inputs or choose a
  source-complete replacement with media regression checks. Recorded BtbN release
  and recipe tag returned 404. Local source evidence is not public-release clearance;
  public source access is not configured. See the distribution licensing record.
- Full newlib notice collection remains conservatively retained; not every section
  has been proven applicable to the shipped Windows binary.
- Investigate the MCP progress-order failure; improve fresh-checkout CI LFS setup.
- Broader Settings redesign stays deferred. Update checks/feed and pending-update
  application stay disabled. No player. Windows is validated; Linux is cross-built,
  macOS packaging is planned, disc capture remains hardware-unverified.
- See [AGENTS](../AGENTS.md), [TODO](todo.md), [publishing](publishing.md),
  [MCP](mcp.md) and the [licence inventory](../licenses/README.md).
