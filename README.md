# KaraokeToolkit

Avalonia desktop app for local karaoke-file conversion. The current build runs on
Windows with bundled FFmpeg for audio conversion. ZIP operations use the
managed SharpCompress NuGet package. Experimental CD+G ripping uses cdrdao; Windows x64 bundles it, while Linux/macOS require an installed executable.
Tests use deterministic media fixtures and explicit format/workflow expectations.
The original MP3+G Toolz binaries and comparison bridges are no longer required.

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
- Optional per-user Windows Explorer context-menu integration under Options → Explorer.
- Read-only karaoke validation and explicit, reversible moves to `Damaged`.

Open **Options → General** for appearance, automatic CUE/TXT generation and existing
output handling. Ask each time offers Skip, Replace and Cancel conversion when a
media or CUE/TXT destination already exists. Disabling automatic generation does not
disable the standalone Create Cue/Text action. Options changes apply on OK or Apply; Cancel
discards changes made since the last Apply.

Settings are stored at `%LOCALAPPDATA%\KaraokeToolkit\options.xmls`. Saves replace the
file atomically and retain the previous version as `options.xmls.bak`.

## Development

Open `KaraokeToolkit.slnx` in Visual Studio with .NET 10 tooling. Projects:

- `KaraokeToolkit.App`: Avalonia views and viewmodels.
- `KaraokeToolkit.Core`: local workflows, media formats and native-tool integration.
- `KaraokeToolkit.Tests`: standalone unit and integration tests, plus opt-in benchmarks.

Build selects a local, checksum-verified FFmpeg archive from `vendor/ffmpeg`.
See [audio runtime and benchmarks](docs/audio-runtime.md) for platforms and packaging,
and [vendor versions](vendor/VERSIONS.md) for the inventory. The
[license inventory](licenses/README.md) lists included notices and remaining
distribution checks.
No original vendor DLLs, .NET Framework compiler/runtime or x86 .NET Desktop Runtime
are needed to build or test. FFmpeg integration tests use the bundled runtime;
disc-reader tests use synthetic images and controlled child processes, without a drive.

```powershell
dotnet build KaraokeToolkit.slnx
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj
```

In the agent sandbox, append `-p:UsedAvaloniaProducts=` if the Avalonia build-statistics
task cannot access its tooling directory. This does not change licensing settings.

The [file workflow features](docs/file-workflow-features.md) cover selection,
reports, Explorer integration, validation and restoring Damaged batches. The
[pre-processing preview](docs/TODO.md) is explicitly deferred.

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
upgrade testing remain pending. Original comparison code and binaries remain
available only in Git history.

Velopack lifecycle integration is present in App, but update checks and automatic
application of pending updates are disabled. No release feed is configured; see
[update integration](docs/updates.md).

## Managed ZIP compression

SharpCompress 0.50.4 is restored through Core's NuGet PackageReference (MIT; see
licenses/SharpCompress.txt). No manual ZIP DLL copies are needed. Production ZIP
operations and tests use no DynaZip, MP3GToolz or BASS assemblies.

Options offers ZIP levels 0–9, defaulting to 5 (Balanced). Level 0 stores files
without compression; levels 1–9 use Deflate, from fastest to smallest size.
Saved numeric levels are preserved. Former named presets retain their actual
levels (0, 1, 6, 9), including saved recovery requests.
BZip2 and LZMA ZIP inputs have extraction tests. Encrypted members are unsupported.
Existing pair, path, CRC, size and transaction checks still apply.
The Repack ZIP operation applies the saved compression settings to existing archives.

ZIP creation also offers BZip2 and LZMA. Deflate remains the default and is
recommended for legacy karaoke players. The 0–9 level setting applies only to
Deflate; BZip2 and LZMA use library defaults. Algorithm choices persist in settings
and recovery requests; older settings without an algorithm use Deflate.

## Convert Archive to ZIP

Choose **Convert Archive to ZIP** in the Convert panel and select files or folders.
Supported input extensions are 7z, rar, tar, gz/tgz, bz2/tbz/tbz2, xz/txz, lz and zst;
actual decoding depends on the format and variant supported by SharpCompress.
The operation preserves all file contents, relative paths, empty files and folders,
and available modification times. It does not restrict archives to karaoke pairs
or recursively unpack archives stored inside other archives. ZIP-specific metadata
cannot represent every source format's permissions, attributes or comments.

Output is beside the source (album.tar.gz becomes album.zip) using the ZIP algorithm
and Deflate level in Options. Existing-output and Delete originals settings apply.
Publication uses the existing transaction/recovery system; originals are removed
only after payload verification and successful publication. Failed items can be
retried. Cancellation keeps the current source. No automatic CUE/TXT is generated.

Encrypted, multi-volume, linked, unsafe or duplicate-path entries are rejected;
individual members must be smaller than 4 GiB. ZIP input is excluded. Generic
archive contents are checked during conversion, rather than using the optional
karaoke-pair validation precheck. Content hashes are compared with the completed ZIP.
Tests include TAR, TGZ, standalone GZip, solid/encrypted 7z, all output algorithms,
source retention, overwrite, cancellation, retry and checkpoint resume.

## Experimental CD+G ripping (cdrdao)

Enable **Options → Audio Encoding → Enable experimental CD+G ripping**, then choose
**Rip CD+G (Experimental)** in the Convert panel. Start opens a dialog to refresh
optical drives, select audio tracks, choose an existing output folder, name the
disc, and select BIN, Audio+G, or ZIP output. Audio bitrate/encoder, ZIP compression,
and overwrite policy come from Options. Disc contents are never modified. CUE/TXT
can be generated afterwards with Validation & Reports → Create CUE/TXT Files.

The cross-platform adapter launches cdrdao, checks the captured TOC against the
selected disc layout, and reads one full-disc image per run. Selected tracks are
extracted with PCM byte swapping and raw CDG deinterleaving. Windows x64 bundles
cdrdao; Linux/macOS currently require it on PATH or `KARAOKETOOLKIT_CDRDAO`.
Reading is cancellable through process isolation. Tracks are staged and
checked for complete 2448-byte sectors and expected length before publication;
converted audio and ZIP outputs use the existing verified pipeline. A cancelled
or failed track is discarded, while already published tracks remain available.
Close the dialog to view its session report in the main window.

This is **not hardware-tested**. Drive firmware behavior, actual subchannel layout,
read accuracy, and scratched-disc handling require a compatible physical drive
and CD+G disc. A structurally complete read does not prove visually correct CD+G
playback. Interrupted disc reads are not automatically resumed: refresh the drive
and select unfinished tracks again. Generic file-conversion recovery is unchanged.

Tests inject `IDiscReader` to supply synthetic sectors or failures, without a drive.
They cover selection, settings, validation, real BIN/Audio+G/ZIP generation,
overwrite decisions, incomplete reads, changed-disc errors, cancellation, no-drive
and dependency-error UI behavior, and staging cleanup. The native hardware calls
remain the explicitly unverified boundary. See [cdrdao implementation and limits](docs/disc-ripping.md), including disc-tail graphics handling and fixture comparison coverage.
