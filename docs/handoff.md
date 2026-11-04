# Current handoff

Snapshot: 2026-11-04, Africa/Johannesburg. Verify Git/files before editing.

## Checkout and authorization

- Branch: `master`; version **2026.11.4** in `Directory.Build.props`.
- Checkpoint commit: **9494fa6** — CLI, shared named operation contracts, documentation
  and version bump. User explicitly requested this commit before the expansion below.
- This versioned commit includes the CLI expansion below and its updated handoff.
  The user requested a version bump, commit and push; use Git history/status for
  the resulting commit ID and remote synchronization.
- Latest steering: CLI always ships with App; no separate CLI release/package is
  needed. The existing publish script continues to combine both executables.
- User authorized all previously listed CLI gaps, including recipes (the prior
  recipe deferral is superseded), disc commands, explicit quarantine, dry runs,
  supported output destinations, help/completion/PATH setup and platform testing.

## Implemented expansion

- 25 Core operation IDs, 22 exposed CLI registrations. QuarantineInvalid is new;
  DiscRip is explicitly registered as a dedicated hardware command. Remaining
  excluded operations are online lookup, playback and update checks.
- `recipe list/show/validate/run`: portable JSON recipes retain per-step settings;
  unattended execution/recovery rejects Ask before mutation. Recipe recovery uses
  its journal snapshot, not the library/exported file. CLI calls Core's existing
  RecipeRunner and recovery rather than reimplementing orchestration.
- `disc-list`, `disc-rip`: cdrdao selection, exact device address, track ranges,
  output format/destination, experimental opt-in and existing conversion options.
  Shared disc preflight/output-path helpers cover dry runs too. Windows CLI build
  targets prepare/package cdrdao; actual physical-drive compatibility is unverified.
- `quarantine` / `run quarantine-invalid`: validate first, move only eligible failed
  groups with existing hashes/manifests. Restore remains available. Validation alone
  is read-only. Quarantine can be previewed.
- OperationPlanner plus read-only engine planning paths preview conversions,
  folder moves/renames, conditional empty-folder removals, restoration and collisions.
  Tag dry runs use tag preview. Recipe plans inspect step 1 and explicitly defer
  later scopes until preceding outputs exist; no speculative execution occurs.
- `--output` for media conversions: existing external flat destination, original
  files kept, target-volume transactions and saved destination-aware recovery.
  Cross-directory deletion is rejected. Multi-folder conversions that generate
  metadata require `--no-cue-text`; generate one listing in the destination later.
  Filename cleanup/listing-only/tagging/validation do not accept separate outputs.
- Per-operation help, dedicated command help, generated PowerShell/Bash/Zsh word
  completion and optional PATH setup scripts. Scripts are printed for opt-in use;
  no profile/environment is modified by these commands. Setup paths are installation
  specific, including versioned Velopack payloads.
- Video verification exposed intermittent FFmpeg `-shortest` overshoot. Rendering
  now also uses an explicit limit from the probed audio duration, preserving the
  intended soundtrack length independently of encoder scheduling.
- CLI examples/reference, README, architecture guidance and TODO updated. See
  [CLI](cli.md), [examples](cli-examples.md) and [operation contracts](operations.md).

## Verification

- Versioned checkpoint: **617 tests passed** (`cli-checkpoint.trx`).
- Expanded suite: **639 tests passed**, Windows (`cli-expanded-full.trx`).
- Built Windows CLI: version is 2026.11.4; JSON discovery has 22 registrations.
  Generated PowerShell completion and PATH scripts parse without applying them.
- One crash test intermittently raced release of killed-process handles. Crash
  helper now waits for journal lease discovery after process exit, preserving the
  existing strict recovery assertions; targeted workflow tests and full suite pass.
- `.github/workflows/cli-platforms.yml` adds manual/PR native-runner tests on Windows,
  Linux, macOS ARM64 and Intel, with native FFmpeg fixtures, CLI executable smoke
  tests, shell syntax checks and TRX artifacts. It does not publish a separate CLI.
- Linux/macOS jobs have **not run here**: no installed WSL distribution or Mac host.
  Do not report their runtime behaviour as verified simply because the workflow exists.
- Physical CD+G hardware testing, clean installation/upgrade testing and macOS
  installer packaging remain outstanding. No new installers were published.

Final full-suite command:

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release --no-restore -p:UsedAvaloniaProducts= -p:ArtifactBuildName=StandaloneTests -v minimal --logger 'trx;LogFileName=cli-expanded-full.trx' --results-directory artifacts/tests/results
```

## Decisions still in force

Update checks/feed and automatic pending updates remain disabled. No player or MCP
executable has been added. See [TODO](todo.md) for remaining release/MCP work and
[AGENTS.md](../AGENTS.md) for architecture, safety, build and handoff rules. Native
licence/source obligations remain in [the licence inventory](../licenses/README.md).

Next step: run the platform workflow on its actual hosts and inspect its results.
Build a combined App/CLI installer when requested; no installer is created by
this version bump/commit/push.
