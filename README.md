# Karaoke Toolkit

Copyright © 2026 André Almeida (ADNA).
Karaoke Toolkit is licensed under **GPL-3.0-or-later**: you may redistribute
and modify it under GNU GPL version 3 or any later version. It comes without
warranty. See [LICENSE](LICENSE) and [the application notice](NOTICE.txt).
Third-party components retain their own licences; see [the inventory](licenses/README.md).

Inspired by MP3+G Toolz, Karaoke Toolkit is an Avalonia desktop app for local
karaoke-file conversion, metadata and file management. Windows is the validated
runtime platform; Windows and Linux x64 packages can be generated locally.

Audio processing uses bundled FFmpeg, ZIP operations use SharpCompress, and
experimental CD+G ripping uses cdrdao. Windows x64 bundles the disc reader;
Linux/macOS require an installed executable. Tests use deterministic media
fixtures and explicit format/workflow expectations.

## Working features

- Audio+G <-> BIN, BIN <-> ZIP, Audio+G <-> ZIP (six conversion actions).
- Repack existing ZIPs using saved compression settings, with Deflate recommended for legacy karaoke-player compatibility. Replaces the original when Delete Originals is enabled, otherwise creates a separate repacked ZIP.
- Lossless Audio+G to MP3+G: matching WAV, FLAC, APE, AIFF, ALAC/M4A or WavPack audio is checked by codec before MP3 encoding; the CDG stays unchanged.
- Convert BIN, karaoke ZIP, or matching Audio+G to MP4, MKV, or AVI video with synchronized CDG graphics and seek-friendly keyframes.
- Local filename cleanup with paired-name and collision protection.
- [Tag Karaoke Files](docs/tagging.md): preview and edit metadata from custom filename templates, with remembered patterns, ZIP-contained audio tagging and media-payload verification.
- Organize files recursively or immediate child folders into A-Z and # folders, with an optional leading-"The" rule for files.
- Flatten one or all directory layers beneath selected folders, moving contents into each selected root while leaving name collisions in place.
- Standalone CUE sheets for BIN tracks and TXT track listings.
- Optional automatic CUE/TXT generation after conversion.
- MP3, OGG, WMA, WAV, FLAC, and AAC output choices, with separate saved bitrates for lossy encoders.
- Video audio bitrate matching from compressed-source metadata, with a configurable fallback for lossless or unknown sources.
- Existing-output policy: Ask each time, Skip, Replace, or Fail without overwriting.
- Persisted System Default, Light and Dark themes.
- Crash recovery at startup or when revisiting an interrupted source folder, including complete recipe continuation and saved tagging batches.
- Drag-and-drop and multi-file/folder selection without converting neighbouring files.
- Completion reports, failed-item retry, output-folder opening and TXT/JSON report export.
- Optional per-user Windows Explorer context-menu integration under Settings → Integrations.
- Read-only karaoke validation and explicit, reversible moves to `Damaged`.

Open **Settings → General** for appearance, automatic CUE/TXT generation and existing
output handling. Ask each time offers Skip, Replace and Cancel conversion when a
media or CUE/TXT destination already exists. Disabling automatic generation does not
disable the standalone Create Cue/Text action. Settings changes apply on OK or Apply; Cancel
discards changes made since the last Apply.

On Windows, settings are stored at `%LOCALAPPDATA%\KaraokeToolkitData\options.xmls`. Saves replace the
file atomically and retain the previous version as `options.xmls.bak`.

## Development

See [the documentation index](docs/README.md) for feature and developer guides.
For agent collaboration, start with [repository guidance](AGENTS.md) and the
[current handoff](docs/handoff.md). Planned/deferred work lives in [TODO](docs/todo.md).

Open `KaraokeToolkit.slnx` in Visual Studio with .NET 10 tooling. Projects:

- `KaraokeToolkit.App`: Avalonia views and viewmodels.
- `KaraokeToolkit.Core`: local workflows, media formats and native-tool integration.
- `KaraokeToolkit.Tests`: standalone unit and integration tests, plus opt-in benchmarks.
- `KaraokeToolkit.Cli`: [command-line operations and recipes](docs/cli.md), shipped with App and usable without a GUI.
- `KaraokeToolkit.Mcp`: [local MCP tools](docs/mcp.md), launched by a client and shipped with App; no hosted service.

Build selects a local, checksum-verified FFmpeg archive from `vendor/ffmpeg`.
See [audio runtime and benchmarks](docs/audio-runtime.md) for platforms and packaging,
and [vendor versions](vendor/versions.md) for the inventory. The
[license inventory](licenses/README.md) lists included notices and remaining
distribution checks.
Builds and tests use .NET 10 without requiring a separate .NET Framework or x86
runtime. FFmpeg integration tests use the bundled runtime;
disc-reader tests use synthetic images and controlled child processes, without a drive.

```powershell
dotnet build KaraokeToolkit.slnx
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj
```

In the agent sandbox, append `-p:UsedAvaloniaProducts=` if the Avalonia build-statistics
task cannot access its tooling directory. This does not change licensing settings.

The [file workflow features](docs/file-workflow-features.md) cover selection,
reports, Explorer integration, validation and restoring Damaged batches. The
[pre-processing preview](docs/todo.md) is explicitly deferred.

Custom build outputs, test reports and generated fixtures use the ignored repository-root
`artifacts/` directory; see [artifact locations](docs/artifacts.md). Normal bin/obj
locations are unchanged.

Tests are organized by responsibility; see [test structure](docs/test-structure.md).
Commit messages follow the [commit conventions](docs/commit-conventions.md).
The ten batch ZIP scenarios run as ordinary xUnit theory cases with explicit expected
payloads, file handling and cancellation outcomes.

## File safety and scope

Conversions stage copies, verify outputs, and publish each source group with rollback
on a commit failure. Requested source deletion occurs only after media output has been
published and checked. Cancel discards unpublished work; earlier completed groups remain.
After a crash, **Recover and resume** checks the interrupted publication and continues
the saved batch without redoing completed groups. **Later** leaves it untouched.
Multi-file publication is not power-loss atomic. Keep pending recovery folders; see
[crash recovery](docs/crash-recovery.md) for behaviour, storage and limitations.

Online renaming and playback remain unavailable. Optical ripping is
experimental and not hardware-tested. Audio and archive conversion use portable
engines; only Windows has been validated end to end.

`scripts/Publish.ps1` generates self-contained Windows x64 installers/portable ZIPs
and Linux x64 AppImages with the repository-pinned Velopack tool. See
[publishing packages](docs/publishing.md). Signing, uploads and real installation/
upgrade testing remain pending.

Velopack lifecycle integration is present in App, but update checks and automatic
application of pending updates are disabled. No release feed is configured; see
[update integration](docs/updates.md).
