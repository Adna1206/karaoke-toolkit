# Documentation

Start with the [project overview](../README.md). Agents should also read
[repository guidance](../AGENTS.md) and the dated [handoff](handoff.md), then verify
the current checkout. [Planned work](todo.md) is a backlog, not implemented behaviour.

## Using the toolkit

| Guide | Covers |
| --- | --- |
| [File workflows](user/file-workflow-features.md) | Selection, archives, conflict review, filename renaming, alphabetical grouping, reports and Explorer integration |
| [Group by Track ID](user/track-grouping.md) | Numeric ranges, masked folders and automatic parents |
| [CLI](user/cli.md) | Headless commands, per-run settings, JSON, exit codes and recovery |
| [MCP](user/mcp.md) | Local stdio tools, client setup, operation exposure and file-change policy |
| [CLI examples](user/cli-examples.md) | Practical conversion, tagging, video, folder and scripting workflows |
| [Tagging](user/tagging.md) | Filename patterns, editable previews, supported formats and tag verification |
| [Normalize Audio Gain](user/audio-gain.md) | Batch loudness matching, peak normalization, fixed gain and output copies |
| [Change Audio Key](user/audio-gain.md#change-audio-key) | Shift karaoke audio in semitones while preserving tempo |
| [Recipes](user/recipes.md) | Saved multi-step operations, step settings, chaining and execution |
| [Crash recovery](user/crash-recovery.md) | Saved runs, workflow continuation, transaction guarantees and limits |
| [Disc ripping](user/disc-ripping.md) | Experimental cdrdao reader, configuration and hardware-test limits |

## Development and distribution

| Guide | Covers |
| --- | --- |
| [Changelog](changelogs/README.md) | Version history, user-visible changes and upgrade steps |
| [App structure](development/app-structure.md) | Avalonia features, views/viewmodels and shared services |
| [Core structure](development/core-structure.md) | Processing responsibilities and namespace organization |
| [Operation contracts](development/operations.md) | Shared identities, capabilities, interface registrations and compatibility |
| [Settings format and migrations](development/settings-format.md) | Canonical profile, migration rules and upgrade compatibility |
| [Activity table messages](development/activity-messages.md) | Item labels, progress and summaries |
| [Desktop UI guidelines](development/ui-definitions.md) | Dialog design, shared presentation, operation and setting metadata |
| [Test structure](development/test-structure.md) | Shared suite, fixtures, coverage and commands |
| [Audio runtime](distribution/audio-runtime.md) | FFmpeg preparation, format checks and opt-in benchmarks |
| [Artifacts](distribution/artifacts.md) | Generated output locations and cleanup |
| [Publishing](distribution/publishing.md) | Windows/Linux packages, prerequisites and release-readiness limits |
| [Updates](distribution/updates.md) | Velopack lifecycle, GitHub Release checks and the settings reset |
| [Versioning](distribution/versioning.md) | Shared YEAR.MONTH.BUILD version |
| [Commit conventions](development/commit-conventions.md) | Conventional Commit messages |

## Planning and dependency records

- [TODO](todo.md): pending features and deferred work.
- [Online lookup research](research/future-online-renaming.md): dated provider notes and
  requirements for a possible future integration.
- [Handoff](handoff.md): current baseline and verification evidence; refresh at
  meaningful stopping points rather than duplicating status in feature guides.
- [Vendor versions](../vendor/versions.md): pinned native distributions and provenance.
- [Licence inventory](../licenses/README.md): shipped notices and distribution obligations.
- [Source availability](distribution/source-availability.md): component versions, source links and verification status for download pages.
- [Distribution licensing](distribution/distribution-licensing.md): verified runtime source matches, source assembly and remaining release obligations.

## Community and direction

- [Contributing](CONTRIBUTING.md)
- [Support](SUPPORT.md)
- [Security policy](SECURITY.md)
- [Code of conduct](CODE_OF_CONDUCT.md)
- [Roadmap](ROADMAP.md)

Keep user, development, distribution and research guides in their respective
subfolders. Use lowercase-kebab-case for guides and conventional uppercase
filenames for the index, roadmap and community files. Keep provider READMEs
beside their vendor inputs and fixture instructions beside test fixtures.
