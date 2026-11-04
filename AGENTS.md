# Working in Karaoke Toolkit

## Start here

1. Inspect `git status --short`, the current branch and recent commits. Preserve
   existing user changes; do not assume the checkout matches a previous chat.
2. Read [the current handoff](docs/handoff.md) and the relevant feature documentation.
3. Use [TODO](docs/todo.md) for the backlog. A TODO records an idea or deferral, not
   permission to implement it. Follow the current user's requested scope.

Repository files and Git are the source of truth. Treat handoff versions, commit
IDs and test counts as dated evidence; verify them before relying on them.

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
  See [CLI](docs/cli.md).
- Keep namespaces aligned with folders. App uses feature folders; Core uses
  responsibility folders. Read [App structure](docs/app-structure.md),
  [Core structure](docs/core-structure.md) and [test structure](docs/test-structure.md).
- Operation presentation metadata lives in App; Core's recipe registry determines
  recipe eligibility. Step settings are saved independently of general preferences.
  See [UI definitions](docs/ui-definitions.md) and [recipes](docs/recipes.md).
- All built-in identities live in Core's `Execution/OperationId`. Persist names,
  never enum numbers; numeric values and ordering are internal details.
  Update Core's catalogue and CLI's explicit
  include/exclude registry together. Their exhaustive switches must keep CS8509
  as an error; do not add catch-all fallbacks or automatic CLI exposure. General
  requests belong in Execution; conversion requests belong in Conversion. See
  [operation contracts](docs/operations.md).
- Preserve persisted operation names and setting keys when reorganizing code.
  The current pre-production identity cleanup deliberately breaks old journals;
  do not add numeric aliases or migration for them. Update references and
  `InternalsVisibleTo` when renaming projects.
- Use [Conventional Commits](docs/commit-conventions.md). The shared release version
  lives in `Directory.Build.props`, with YEAR.MONTH.BUILD and BUILD starting at 1
  for a new month. Bump versions when requested; see [versioning](docs/versioning.md).

## Build, test and publish

Use .NET 10. From the repository root:

```powershell
dotnet build KaraokeToolkit.slnx
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release
.\scripts\Publish.ps1
```

In the agent sandbox, the established test command is:

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests -v minimal --logger trx --results-directory artifacts/tests/results
```

- Tests include deterministic media, native FFmpeg integration and disposable
  process-crash workers. Prefer automated coverage over repeatedly driving the GUI;
  use targeted UI verification when the change needs it. Do not duplicate Core
  conversion tests in CLI/MCP adapters.
- Native FFmpeg archives are pinned under `vendor/`; build targets verify and select
  them by RID. Tests use self-contained fixtures and explicit expected outcomes.
- Custom outputs belong under ignored `artifacts/`; normal bin/obj are unchanged.
  Keep generated media, installers and test reports out of source control. See
  [artifact locations](docs/artifacts.md) and [audio runtime](docs/audio-runtime.md).
- Publishing is explicit, not an ordinary Release post-build step. The script
  creates self-contained Windows/Linux x64 packages and restores a repository-local
  `vpk` tool. Keep `.config/dotnet-tools.json` aligned with the App's Velopack version.
  See [publishing](docs/publishing.md). It does not upload or install packages.
- After relevant changes, run appropriate tests and record the actual command,
  result and limitations. Do not claim installation/hardware/platform verification
  from a successful package build alone. Documentation-only edits need link/diff
  checks rather than the complete runtime suite.

## Decisions to preserve

- Velopack checks stay disabled and the feed unconfigured until the user requests
  the hosting/update UI work. Automatic application of pending updates is disabled.
  See [updates](docs/updates.md).
- No karaoke player/preview playback yet; it is deferred for discussion. A planned
  processing preview means outputs/conflicts/deletions, not media playback.
- CLI calls shared C# execution services/Core directly. MCP remains planned and
  should reuse those services, not shell out to CLI commands. Initial MCP is
  local stdio, client-launched, independent of the GUI and a hosted web server.
- The proposed MCP setup page supplies instructions/path/copyable configuration,
  without a GUI enable/disable switch. See TODO for the full agreed scope.
- Existing Windows Explorer integration is classic/per-user. Modern Windows 11
  shell integration and an additional MSI are future work, not delivered features.
- File-changing operations must retain existing transaction, overwrite, validation
  and recovery guarantees. Read [crash recovery](docs/crash-recovery.md) before
  changing publication/checkpoint behaviour. Completed workflow changes are not
  rolled back as an entire recipe.
- CLI output-folder conversions keep originals and use destination-volume staging
  with saved destination-aware recovery. Do not extend this to cross-directory
  source deletion without a durable transaction design. Dry runs never invoke
  publishing; later recipe-step scopes are explicitly deferred until outputs exist.
- Windows is the validated runtime platform. Linux packages are cross-built;
  macOS packaging via a macOS CI runner is a TODO. Disc ripping is experimental
  and not verified with physical hardware. See [disc ripping](docs/disc-ripping.md).
- Preserve/update shipped third-party notices and About entries when dependencies
  change. [Licence inventory](licenses/README.md) records remaining distribution
  obligations; generated packages are not evidence those obligations are complete.

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

Documentation uses lowercase-kebab-case filenames, except conventional `README.md`
entry points and this tool-discovered `AGENTS.md`. Use [the docs index](docs/README.md)
for navigation. Describe current behaviour and planned work separately; avoid
duplicating dated investigation or test-run narratives across feature guides.
