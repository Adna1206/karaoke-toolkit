# Documentation

Start with the [project overview](../README.md). Agents should also read
[repository guidance](../AGENTS.md) and the dated [handoff](handoff.md), then verify
the current checkout. [Planned work](todo.md) is a backlog, not implemented behaviour.

## Using the toolkit

| Guide | Covers |
| --- | --- |
| [File workflows](file-workflow-features.md) | Selection, archives, compression, folder tools, reports, validation and Explorer integration |
| [CLI](cli.md) | Headless commands, per-run settings, JSON, exit codes and recovery |
| [MCP](mcp.md) | Local stdio tools, client setup, operation exposure and file-change policy |
| [CLI examples](cli-examples.md) | Practical conversion, tagging, video, folder and scripting workflows |
| [Tagging](tagging.md) | Filename patterns, editable previews, supported formats and tag verification |
| [Recipes](recipes.md) | Saved multi-step operations, step settings, chaining and execution |
| [Crash recovery](crash-recovery.md) | Saved runs, workflow continuation, transaction guarantees and limits |
| [Disc ripping](disc-ripping.md) | Experimental cdrdao reader, configuration and hardware-test limits |

## Development and distribution

| Guide | Covers |
| --- | --- |
| [Changelog](changelogs/README.md) | Version history, user-visible changes and upgrade steps |
| [App structure](app-structure.md) | Avalonia features, views/viewmodels and shared services |
| [Core structure](core-structure.md) | Processing responsibilities and namespace organization |
| [Operation contracts](operations.md) | Shared identities, capabilities, interface registrations and compatibility |
| [UI definitions](ui-definitions.md) | Operation/setting metadata and presentation preferences |
| [Test structure](test-structure.md) | Shared suite, fixtures, coverage and commands |
| [Audio runtime](audio-runtime.md) | FFmpeg preparation, format checks and opt-in benchmarks |
| [Artifacts](artifacts.md) | Generated output locations and cleanup |
| [Publishing](publishing.md) | Windows/Linux packages, prerequisites and release-readiness limits |
| [Updates](updates.md) | Velopack lifecycle and disabled update-check scaffold |
| [Versioning](versioning.md) | Shared YEAR.MONTH.BUILD version |
| [Commit conventions](commit-conventions.md) | Conventional Commit messages |

## Planning and dependency records

- [TODO](todo.md): pending features and deferred work.
- [Online lookup research](future-online-renaming.md): dated provider notes and
  requirements for a possible future integration.
- [Handoff](handoff.md): current baseline and verification evidence; refresh at
  meaningful stopping points rather than duplicating status in feature guides.
- [Vendor versions](../vendor/versions.md): pinned native distributions and provenance.
- [Licence inventory](../licenses/README.md): shipped notices and distribution obligations.
- [Distribution licensing](distribution-licensing.md): verified runtime source matches, source assembly and remaining release obligations.

Keep this directory flat while the guides remain easy to navigate through this
index. Use lowercase-kebab-case filenames for guides; conventional `README.md`
entry points and root `AGENTS.md` retain their established names. Keep provider
READMEs beside their vendor inputs and fixture instructions beside test fixtures.
