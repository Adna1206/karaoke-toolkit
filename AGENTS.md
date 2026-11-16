# Working in Karaoke Toolkit

## Start here

1. Inspect `git status --short`, the current branch and recent commits. Preserve
   existing user changes; do not assume the checkout matches a previous chat.
2. Read [the current handoff](docs/handoff.md) and the relevant feature documentation.
3. Use [TODO](docs/todo.md) for the backlog. A TODO records an idea or deferral, not
   permission to implement it. Follow the current user's requested scope.

Repository files and Git are the source of truth. Treat handoff versions, commit
IDs and test counts as dated evidence; verify them before relying on them.

## Repository standards

Use the guides relevant to the change. The [docs index](docs/README.md) lists
the remaining feature and reference guides.

| When changing | Standards to consult |
| --- | --- |
| Commits or release versions | [Commit conventions](docs/development/commit-conventions.md), [versioning](docs/distribution/versioning.md), [changelog](docs/changelogs/README.md) |
| Project layout or tests | [App structure](docs/development/app-structure.md), [Core structure](docs/development/core-structure.md), [test structure](docs/development/test-structure.md) |
| Operation IDs, requests, or interface exposure | [Operation contracts](docs/development/operations.md), [CLI](docs/user/cli.md), [MCP](docs/user/mcp.md), [recipes](docs/user/recipes.md) |
| Desktop metadata, settings, or activity rows | [UI definitions](docs/development/ui-definitions.md), [activity table messages](docs/development/activity-messages.md) |
| File publication, overwrite, or resume | [Crash recovery](docs/user/crash-recovery.md) |
| Build outputs, native runtime, or release packaging | [Artifacts](docs/distribution/artifacts.md), [audio runtime](docs/distribution/audio-runtime.md), [publishing](docs/distribution/publishing.md), [updates](docs/distribution/updates.md) |
| Dependencies, notices, or distribution obligations | [Licence inventory](licenses/README.md), [distribution licensing](docs/distribution/distribution-licensing.md), [vendor versions](vendor/versions.md) |

## Architecture and conventions

- `KaraokeToolkit.Core`: GUI-independent processing engines, requests/options,
  reports, recipes, file safety and recovery. No Avalonia or updater dependency.
- `KaraokeToolkit.App`: Avalonia desktop UI, viewmodels, presentation metadata,
  desktop preferences, Explorer integration and Velopack lifecycle/update service.
- `KaraokeToolkit.Tests`: shared xUnit suite for Core, App and CLI, grouped by feature.
  This project was renamed from Core.Tests; use the current name in commands.
- `KaraokeToolkit.Cli`: console adapter over Core's unattended execution services.
  No GUI dependency. Includes recipes/recovery, explicit quarantine, dry runs and
  experimental disc capture. Never prompt or elevate; recipe steps cannot use Ask.
  See [CLI](docs/user/cli.md).
- `KaraokeToolkit.Mcp`: client-launched local stdio tool provider over Core, with
  explicit operation include/exclude decisions and CS8509 as an error. No CLI/App
  dependency, listening port or background service. Read-only by default; writes
  require owner startup `--allow-writes` and per-call confirmation. See [MCP](docs/user/mcp.md).
- Keep namespaces aligned with folders. App uses feature folders; Core uses
  responsibility folders. Read [App structure](docs/development/app-structure.md),
  [Core structure](docs/development/core-structure.md) and [test structure](docs/development/test-structure.md).
- Operation presentation metadata lives in App; Core's recipe registry determines
  recipe eligibility. Step settings are saved independently of general preferences.
  See [UI definitions](docs/development/ui-definitions.md) and [recipes](docs/user/recipes.md).
- All built-in identities live in Core's `Execution/OperationId`. Persist names,
  never enum numbers; numeric values and ordering are internal details.
  Update Core's catalogue and CLI's explicit
  include/exclude registry together. Their exhaustive switches must keep CS8509
  as an error; do not add catch-all fallbacks or automatic CLI exposure. General
  requests belong in Execution; conversion requests belong in Conversion. See
  [operation contracts](docs/development/operations.md).
- Preserve persisted operation names and setting keys when reorganizing code.
  The current pre-production identity cleanup deliberately breaks old journals;
  do not add numeric aliases or migration for them. Update references and
  `InternalsVisibleTo` when renaming projects.
- Use [Conventional Commits](docs/development/commit-conventions.md). The shared release version
  lives in `Directory.Build.props`, with YEAR.MONTH.BUILD and BUILD starting at 1
  for a new month. Bump versions when requested. With each bump, create or
  update the release's changelog and link it at the top of the [changelog
  index](docs/changelogs/README.md). Follow the [versioning guide](docs/distribution/versioning.md).

## Build, test and publish

Use .NET 10. From the repository root:

```powershell
dotnet build KaraokeToolkit.slnx
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release
.\scripts\Publish.ps1
```

In the agent sandbox, the established test command is:

```powershell
.scriptsTest.ps1
```

- Tests include deterministic media, native FFmpeg integration and disposable
  process-crash workers. Prefer automated coverage over repeatedly driving the GUI;
  use targeted UI verification when the change needs it. Do not duplicate Core
  conversion tests in CLI/MCP adapters.
- Native runtime manifests under `vendor/` pin exact URLs and SHA-256 digests. Build targets fetch cache misses by RID and verify archives before extraction. Tests use self-contained fixtures and explicit expected outcomes.
- Follow the shared desktop activity conventions in [UI definitions](docs/development/ui-definitions.md).
  Emit structured source and label fields for item rows. Keep count-only updates
  available to progress bars without repeating them in the activity table.
- If the sandboxed Windows build succeeds and VSTest times out connecting to
  testhost, retry with local test-host access using the same built output.
  `DOTNET_CLI_TELEMETRY_OPTOUT=1` avoids a registry access failure in this sandbox.
- Reuse the StandaloneTests build output for serial verification. Use separate
  output names when concurrent runs or deliberate comparisons need isolation.
- Custom outputs belong under ignored `artifacts/`; normal bin/obj are unchanged.
  Keep generated media, installers and test reports out of source control. See
  [artifact locations](docs/distribution/artifacts.md) and [audio runtime](docs/distribution/audio-runtime.md).
- Publishing is explicit, not an ordinary Release post-build step. The script
  creates self-contained Windows/Linux x64 packages and restores a repository-local
  `vpk` tool. Keep `.config/dotnet-tools.json` aligned with the App's Velopack version.
  See [publishing](docs/distribution/publishing.md). It does not upload or install packages.
- After relevant changes, run appropriate tests and record the actual command,
  result and limitations. Do not claim installation/hardware/platform verification
  from a successful package build alone. Documentation-only edits need link/diff
  checks rather than the complete runtime suite.

## Decisions to preserve

- Velopack checks GitHub Releases manually or at startup when opted in. Automatic application of pending updates is disabled.
  See [updates](docs/distribution/updates.md).
- No karaoke player/preview playback yet; it is deferred for discussion. A planned
  processing preview means outputs/conflicts/deletions, not media playback.
- CLI and MCP call shared Core services directly; never shell out to each other.
  MCP is local stdio, client-launched and independent of the GUI or a hosted server.
  Check its explicit catalogue for direct calls, recipe steps and recovery alike.
- Settings → Integrations → MCP supplies path/copyable client configuration. Its
  file-access dropdown defaults to Read only; Allow file changes adds
  `--allow-writes` only to that example;
  MCP reads startup arguments, never App preferences. No GUI service switch.
- Existing Windows Explorer integration is classic/per-user. Modern Windows 11
  shell integration and an additional MSI are future work, not delivered features.
- File-changing operations must retain existing transaction, overwrite, validation
  and recovery guarantees. Read [crash recovery](docs/user/crash-recovery.md) before
  changing publication/checkpoint behaviour. Completed workflow changes are not
  rolled back as an entire recipe.
- CLI output-folder conversions keep originals and use destination-volume staging
  with saved destination-aware recovery. Do not extend this to cross-directory
  source deletion without a durable transaction design. Dry runs never invoke
  publishing; later recipe-step scopes are explicitly deferred until outputs exist.
- Windows is the primary target and validated runtime platform, but aim to keep
  shared functionality compatible across platforms. Prefer portable APIs; isolate
  unavoidable OS-specific code behind platform guards and preserve usable behaviour
  elsewhere. Distinguish intended compatibility from verified platform support.
  Linux packages are cross-built;
  macOS packaging via a macOS CI runner is a TODO. Disc ripping is experimental
  and not verified with physical hardware. See [disc ripping](docs/user/disc-ripping.md).
- Preserve/update shipped third-party notices and About entries when dependencies
  change. [Licence inventory](licenses/README.md) records remaining distribution
  obligations; generated packages are not evidence those obligations are complete.
- Copy all application and third-party licence texts, notices and displayed source
  information into the output `licenses/` tree. About reads only that tree; use
  component subfolders and retain origin/component/document labels. Root repository
  LICENSE/NOTICE entry points and notices inside pinned upstream archives may remain.
- Name maintained native notice copies `Component.Document.txt` under their tool's
  licence subfolder; derive display labels from those names. Keep audit metadata
  available as documentation rather than listing it as a licence in About.

## Maintain this guidance

Agents may extend, clarify, reorganize and trim this file during authorized work
without asking about routine edits. Keep it concise, remove duplication, correct
verified stale paths/commands, and record decisions the user has already approved.
Summarize meaningful guidance changes in the handoff or completion message.

Check with the user before applying a major change to this guidance: introducing
or removing a policy, changing architecture or release workflow expectations,
expanding agent permissions, or reversing a preserved decision. Present the proposed
change and reason first. Size alone does not determine significance; a one-line
permission change can be major. An explicit instruction in the current conversation
already authorizes the corresponding guidance update and needs no second approval.
This maintenance permission does not authorize unrelated code refactors or backlog
implementation, and must not be used to remove the requirement to check major changes.

## Leave a useful handoff

At meaningful stopping points, update `docs/handoff.md` with the dated branch/base
commit, current version, completed work, uncommitted changes, verified commands and
results, open limitations, and next concrete task or decision. Keep it a short
snapshot, not a transcript; link detailed documentation instead of copying it.
Keep lasting rules here and backlog items in TODO. Clearly distinguish implemented,
tested and merely planned behaviour. Never record credentials in handoff documents.

Use fictional karaoke catalogue prefixes such as "ABC" in documentation examples.
Documentation uses lowercase-kebab-case filenames, except conventional `README.md`
entry points and this tool-discovered `AGENTS.md`. Use [the docs index](docs/README.md)
for navigation. Describe current behaviour and planned work separately; avoid
duplicating dated investigation or test-run narratives across feature guides.
