# Current handoff

Snapshot: 2026-11-05, Africa/Johannesburg. Verify Git/files before editing.

## Version 2026.11.8

- Bumped the shared version from 2026.11.7 on `master` / `f71ff64` for the
  GPL application licence, dependency source/notice work and installer naming.
- Release suite: `dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj
  -c Release --no-restore -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests
  -v minimal --logger trx --results-directory artifacts/tests/results` ran outside
  the sandbox: 668 passed, one MCP progress-order assertion failed. The failing
  test passed on a targeted `--no-build --no-restore` rerun. No package published.
- Next: distinguish cdrdao runtime notices in About and omit the component inventory
  from the in-app notice list; retain the inventory in the package.

## Application licence applied

- Owner selected the recommended **GPL-3.0-or-later** on 2026-11-05. Copyright
  © 2026 André Almeida (ADNA). Root LICENSE contains the unmodified
  GPL v3 text; NOTICE.txt grants v3-or-later for original code/assets and preserves
  third-party terms. README, build metadata and licensing documentation updated.
- Owner subsequently shortened the public name to **André Almeida (ADNA)**;
  notices, About, metadata and documentation now use it consistently. MSBuild
  copyright evaluation and diff checks passed; text-only follow-up, no test rerun.
- Core content items copy LICENSE/NOTICE.txt to Core/App/CLI/MCP outputs and
  publish payloads; publisher checks for both. About has a separate Application
  licence tab and redistribution/no-warranty notice. Source evidence assembly
  also includes the application licence for its accompanying documentation.
- Targeted About tests passed **2/2** with the established Release command,
  `--no-restore`, `ArtifactBuildName=StandaloneTests`, filter
  `FullyQualifiedName~AboutViewModelTests`, minimal verbosity and TRX output.
  Sandboxed invocation failed without diagnostics; identical unsandboxed run passed.
  MSBuild metadata/content evaluation and full GPL text hash comparison passed.
  No visual UI verification, full suite, package publish, version bump or commit.
- Third-party source audit and installer-name edits remain uncommitted. Application
  licence decision is resolved; FFmpeg source-completeness work remains open below.

## Installer filename follow-up

- On `master` / `f71ff64`, version 2026.11.7, updated `scripts/Publish.ps1`
  to append the evaluated app version to Windows Setup EXE filenames before
  generating checksums, e.g. `KaraokeToolkit-win-Setup-2026.11.7.exe`.
- PowerShell parser check and execution of the actual rename block against
  disposable artifact fixtures passed; update-package filename preserved.
  `git diff --check` passed. No full publish, version bump or installer run.
  Existing licensing edits preserved; no change to that work's open decisions.

## Licensing follow-up

- Authorized review/work on application licensing and dependency source/notices,
  based on `master` / `f71ff64`, version 2026.11.7 unchanged. Prior handoff
  assessment edits preserved. Application licence is now GPL-3.0-or-later as
  recorded above; copyright holder: André Almeida (ADNA).
- Matched all four cdrdao runtime DLLs byte-for-byte to archived MSYS2 binary
  packages; recorded versions/hashes/source mappings, retained runtime licence
  texts and added copying plus hash checks to cdrdao preparation.
- Added checksummed source evidence manifest and explicit offline-by-default
  source assembly script. Six source archives retrieved into ignored audit cache;
  source assembly output is under `artifacts/distribution-sources/`.
- Verified with Windows PowerShell: `scripts/cdrdao/Prepare.ps1 -RuntimeIdentifier
  win-x64 -OutputDirectory artifacts/checks/licensing/prepared-cdrdao` and
  `scripts/Prepare-DistributionSources.ps1` both passed. Sources include MSYS2
  upstream code/patches/recipes, cdrdao, TagLibSharp release and FFmpeg core.
  This is not full-suite, installer or complete source-compliance verification.
- Remaining: FFmpeg external-library revisions/notices/build inputs are unresolved;
  recorded BtbN release and recipe tag return 404. No binary replacement, version
  bump, republish, commit or upload. Public source
  access is not configured. See [distribution licensing](distribution-licensing.md).
- Next decision: recover exact FFmpeg build inputs or authorize a source-complete
  replacement build with media regression checks.

## Repository assessment follow-up

- Reviewed on 2026-11-05 at `master` / `f71ff64`, version 2026.11.7;
  checkout initially clean. No product changes; this snapshot is the only edit.
- Sampled architecture, transactions, MCP access policy, tests and CI. Fresh-runner
  concern: native archives use Git LFS but `cli-platforms.yml` checkout does not
  enable LFS retrieval; verify/fix before relying on that workflow.
- Attempted the established Release test command with `ArtifactBuildName=StandaloneTests`,
  TRX logger and `artifacts/tests/results`; exited 1 before tests. Retried with
  `--no-restore`, including diagnostic verbosity: build failed with no useful error
  reported. Diagnostic output: ignored `artifacts/repo-review-test.log`.
  The earlier 669-pass result below was not reproduced during this assessment.
- Suggested next decision: prioritize fresh-checkout CI and Windows installation/
  recovery verification before expanding features. Public distribution obligations
  remain recorded in the licence inventory; no release work authorized here.

## Checkout and release

- Branch `master`; shared version **2026.11.7** in `Directory.Build.props`.
- Latest user request: bump, commit and push the GUID definition/version, based
  on `de33f72`. No republish requested; existing installers remain 2026.11.6.
  Metadata-only follow-up: evaluated MSBuild properties and diff checked; full
  runtime suite not repeated. Check Git for final commit/push state.
- Fixed `ApplicationGuid` defined in `Directory.Build.props`,
  generated once using .NET's GUID generator. Definition only; existing Velopack
  package identity stays `KaraokeToolkit`. MSBuild property evaluation verified.
- Follow-up to release `0c24904`: Settings heading expanded to Command Line
  Interface (CLI). Text-only change; diff checked, tests not rerun. Existing
  packages predate this heading change; no version bump or republish requested.
- This release commit follows `e7b39b6` (CLI settings/320 kbps defaults). User
  authorized version bump, commit and local publish. No push requested for this
  release; last known pushed base is `f36c40e`. Check Git for actual commit ID.
- Windows/Linux packages contain App, CLI and MCP together, sharing runtime/Core
  and native tools. Ordinary builds retain separate project bin/obj outputs.
- Whole-Settings redesign remains stopped in [TODO](todo.md). The latest changes
  were limited to the requested Integrations layout and MCP setup controls.

## Completed MCP work

- New `KaraokeToolkit.Mcp` .NET 10 console adapter calls Core directly, independent
  of App/CLI. Official C# SDK 2.3.1, local client-launched stdio, stderr diagnostics,
  no HTTP listener/background service/scripts/elevation. Nine tools cover discovery,
  settings, validation, tag preview, planning, operations, recipes and recovery.
- Explicit catalogue decides all 25 Core IDs (21 included). Exclusions carry reasons;
  direct calls, recipe steps and recovery all enforce it. CS8509 remains an error.
  Unknown CLI/MCP operation errors now include discovery guidance; excluded calls
  include their reason. CLI human/JSON errors retain exit 2.
- Read-only startup is default. Mutations require `--allow-writes` plus per-call
  `confirmFileChanges=true` for authorized work. No per-file popup is mandated.
  This is not a directory sandbox; the confirmation boolean records caller intent.
- Strict options accept camelCase/PascalCase, named enums and ZIP levels 0-9;
  unknown/duplicate settings reject. Omitted deletion options keep originals.
  No App preferences read. Recipe step snapshots retained. Processing serialized;
  cancellation reaches Core and progress is monotonic. Completed groups stay committed.
- Settings → Integrations has CLI and MCP collapsible sections, both initially
  expanded, on one scrolling page. No nested tabs or integration selector.
  MCP shows executable path/Copy Path; Client configuration contains Read only /
  Allow file changes dropdown, JSON and Copy Client Configuration. Choice defaults
  to Read only, is not persisted and only changes generated arguments. It never
  starts MCP, alters runtime policy or edits client files. App has no MCP dependency.
- Shared Microsoft.Extensions logging/DI aligned at 10.0.13 for combined payload.
  SDK Apache/MIT notices shipped and About/licence inventory updated. AGENTS reflects
  delivered MCP and configuration-only setup. See [MCP guide](mcp.md).

## Verification and local outputs

- Full suite **669 passed, 0 failed** on Windows after version bump:

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -p:UsedAvaloniaProducts= -p:ArtifactBuildName=ReleaseCheck -v minimal --logger 'trx;LogFileName=release-2026.11.6.trx' --results-directory artifacts/tests/results
.\scripts\Publish.ps1
```

- Includes 18 MCP tests using real SDK stdio clients for discovery, permission gates,
  extraction, recipes, recovery, progress, strict settings and actionable errors.
  Settings layout subset also passed 47 tests (`integrations-sections.trx`).
- Earlier isolated compiler probe under ignored artifacts correctly failed CS8509
  after appending a future enum member in a copy; source enum unchanged.
- Publish succeeded for **win-x64 and linux-x64**, run `20261105-055200-0155a3`:
  - Packages: `artifacts/releases/2026.11.6/20261105-055200-0155a3/<rid>/`
  - Payloads: `artifacts/publish/2026.11.6/20261105-055200-0155a3/<rid>/`
  - Windows Setup EXE/portable ZIP, Linux AppImage, update packages and SHA256SUMS.
- Published Windows MCP initialized, discovered nine tools and answered get_operations
  through the official SDK client (`artifacts/checks/mcp-packaged-smoke/`).
- No installer installed or package uploaded. Unsigned outputs. Linux is cross-built,
  not runtime-verified; macOS, physical disc hardware and clean install/upgrade
  verification remain outstanding. No visual check of latest Settings layout.

## Existing decisions and next work

320 kbps audio defaults; independent video matching/192 kbps fallback. CLI destination
conversions keep originals and preserve destination-aware recovery. Update checks/feed
and pending update application stay disabled. No player. Linux disc capture needs
installed cdrdao/configured path. See [AGENTS](../AGENTS.md), [TODO](todo.md),
[publishing](publishing.md) and [licence inventory](../licenses/README.md).

Next: user can install the 2026.11.6 Windows package, configure an MCP client and
verify native platforms. Broader Settings redesign needs authorization.
