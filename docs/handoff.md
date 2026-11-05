# Current handoff

Snapshot: 2026-11-05, Africa/Johannesburg. Verify Git/files before editing.

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
