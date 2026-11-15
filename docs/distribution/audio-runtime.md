# Audio runtime and benchmarks

Audio decoding and MP3/OGG/WMA/WAV/FLAC/AAC encoding use FFMpegCore 5.5.0 (MIT),
restored by NuGet.
FFMpegCore invokes bundled FFmpeg/ffprobe processes; NuGet does not supply those
executables. Experimental CD reading uses cdrdao.
See [disc ripping](../user/disc-ripping.md) for packaging and hardware-test limitations.
Tests assert deterministic PCM, graphics, duration and timing expectations.

## Runtime preparation

Directory.Build.targets runs native runtime preparation for the selected .NET
Runtime Identifier. On a cache miss it downloads the exact archive or archive
pair pinned in the vendor manifests and verifies SHA-256 before extraction.
Downloads are cached in `artifacts/runtime/downloads/<sha256>/`. The extracted
FFmpeg distribution goes into the output's `ffmpeg/` directory. Windows
preparation also fetches cdrdao and its release source. A warm cache supports
offline preparation. The application does not fetch binaries at startup.

Use `./scripts/Prefetch-Runtimes.ps1 -RuntimeIdentifier win-x64,linux-x64,osx-arm64`
to warm individual target caches ahead of time. If an upstream build disappears,
restore a copy with the exact expected SHA-256 to its cache path. Builds fail
with the URL and digest when a pin cannot be fetched and verified.

`win-x64` and `linux-x64` use pinned BtbN LGPL shared builds. The macOS target
uses separate Martin Riedl FFmpeg/FFprobe ZIPs for Apple Silicon. They contain GPL code. The paired
ZIPs are assembled into `bin/` with the applicable GPL text and provider notice.
Linux/macOS build hosts need PowerShell 7 (`pwsh`); tar is needed for tar archives.
Build preparation sets executable permissions on macOS when run there. Cross-
publishing from Windows does not verify macOS execution or signing. Media tests
use the native runtime for their build target.

`-p:SkipFFmpegPrepare=true` is for compilation-only checks; it does not produce a
runnable package unless the runtime is already present.

## Licensing and distribution

The pinned Windows and Linux FFmpeg builds are LGPL **version 3** (they enable
version3, without GPL or nonfree components). Their `LICENSE.txt`, the GPL v3
text incorporated by LGPL v3, and `runtime-manifest.json` are included in the
FFmpeg output folder and copied to `licenses/ffmpeg/`, which About reads.
The app also publishes common package notices from
[`licenses/`](../../licenses/README.md). Each RID has its own version, hash and
source metadata in the manifest.

Before distributing a release, retain and provide the corresponding source and
build information required by the licenses of FFmpeg and its bundled dependencies.
A source-repository link alone is not a complete corresponding-source bundle.
The preparation script preserves binary notices; it does not assemble that source
bundle. See https://ffmpeg.org/legal.html for upstream guidance.

The macOS builds are for local development until they have been run on their
target architectures and their GPL corresponding-source obligations are met.
See [distribution licensing](distribution-licensing.md) for the remaining macOS source and provenance checks.

## Behavior and tests

BIN audio remains 44.1 kHz, stereo, signed 16-bit PCM. Inputs with another sample
rate/channel count are rejected rather than silently resampled. Encoding uses
the selected format and applicable bitrate options. Encoded output is decoded and its duration
checked before publication. Processes support cancellation and a 30-minute limit;
existing staging, overwrite and recovery behavior remains in charge of publication.
Local file/pipe protocols are allowed; network input protocols are not enabled.

Convert Lossless Audio+G to MP3+G accepts one same-named CDG and lossless audio
file: WAV/AIFF PCM, FLAC, APE, ALAC in M4A, or WavPack. FFprobe must identify
the actual codec as lossless; an AAC M4A or compressed WAV is rejected even if
its extension is accepted. The source must be stereo; FFmpeg resamples higher
or lower rates to 44.1 kHz for MP3 output. Upsampling cannot restore missing
detail. The operation uses the configured MP3 bitrate, checks the decoded output duration, preserves
the CDG, and removes only the old audio when Delete old files is enabled.

FFmpegAudioTests checks exact lossless WAV/FLAC PCM, lossy MP3/OGG sample alignment,
invalid input, unsupported channel count, cancellation and output preservation.
The standalone conversion and archive contract tests exercise the surrounding code.

## Running benchmarks

The small explicit harness lives in KaraokeToolkit.Tests/Benchmarks and uses
the existing test executable. It is not discovered by xUnit; normal tests never
fail because one codec took longer. No separate benchmark project is needed.

```powershell
dotnet build KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release
dotnet KaraokeToolkit.Tests/bin/Release/net10.0/KaraokeToolkit.Tests.dll --benchmark-audio 3
```

The optional count is 1–20 measured iterations after one warmup per case.
Results, individual timings and generated inputs/outputs go under
artifacts/benchmarks/audio/<run-id>/, including results.md and results.json.
Keep the machine idle and run with the same configuration when comparing results.

Cases use shared deterministic 2-second and 30-second stereo multitone audio:
WAV/MP3/OGG/FLAC decoding; MP3/OGG encoding plus verification at 192 kbps; and
BIN split, MP3 encode, verification, ZIP creation and verification. Pipeline cases
use the managed ZIP engine. GUI, final publication and source deletion are
excluded. The harness measures FFmpeg only, for comparison across builds. Wall-clock measurements include
process startup and disk I/O; this is a practical smoke benchmark, not a codec-only
microbenchmark or a listening-quality assessment. Lossy sample/padding differences
are expected. Real music and longer tracks may behave differently.

Results include one FFmpeg measurement per case, output sizes, hashes and PCM
durations. Compare reports from the same machine, runtime and corpus; timing is
never a unit-test pass/fail gate.
