# Audio runtime and benchmarks

Audio decoding and MP3/OGG encoding use FFMpegCore 5.5.0 (MIT), restored by NuGet.
FFMpegCore invokes bundled FFmpeg/ffprobe processes; NuGet does not supply those
executables. BASS, lame.exe and oggenc.exe are no longer used for file conversion.
Experimental CD reading uses cdrdao. No original native libraries are used by tests.
See [disc ripping](disc-ripping.md) for packaging and hardware-test limitations.
Tests assert deterministic PCM, graphics, duration and timing expectations without
the original audio engine.

## Runtime preparation

Directory.Build.targets runs scripts/ffmpeg/Prepare.ps1 after App and Tests builds
and includes its output in App publishing. The script reads only the archive or
archive pair for the selected .NET Runtime Identifier from `vendor/ffmpeg/<rid>/`,
verifies SHA256 against `vendor/ffmpeg/manifest.json`, extracts it into the ignored
`artifacts/runtime/ffmpeg/<version>/<rid>` cache, and copies the complete
distribution to `output/ffmpeg`. It never downloads binaries, during either build
or app startup. The complete distribution includes shared libraries and notices.
The build fails if the selected RID has no archive or its checksum differs.

`win-x64` and `linux-x64` use pinned BtbN LGPL shared builds. The macOS targets
use separate static FFmpeg/FFprobe ZIPs: Evermeet for Intel (`osx-x64`) and
OSXExperts for Apple Silicon (`osx-arm64`). They contain GPL code. The paired
ZIPs are assembled into `bin/` with the applicable GPL text and provider notice.
Linux/macOS build hosts need PowerShell 7 (`pwsh`); tar is needed for tar archives.
Build preparation sets executable permissions on macOS when run there. Cross-
publishing from Windows does not verify macOS execution or signing. Media tests
use the native runtime for their build target; no Windows-only reference engine is needed.

`-p:SkipFFmpegPrepare=true` is for compilation-only checks; it does not produce a
runnable package unless the runtime is already present. The retained Windows
archive is a pinned BtbN build; its upstream daily release may expire, but normal
builds now use the vendored copy. Store large archives with Git LFS.

## Licensing and distribution

The vendored Windows and Linux FFmpeg builds are LGPL **version 3** (they enable
version3, without GPL or nonfree components). Their `LICENSE.txt`, the GPL v3
text incorporated by LGPL v3, and `runtime-manifest.json` are included in the
FFmpeg output folder. The app also publishes common package notices from
[`licenses/`](../licenses/README.md). Each RID has its own version, hash and
source metadata in the manifest.

Before distributing a release, retain and provide the corresponding source and
build information required by the licenses of FFmpeg and its bundled dependencies.
A source-repository link alone is not a complete corresponding-source bundle.
The preparation script preserves binary notices; it does not assemble that source
bundle. See https://ffmpeg.org/legal.html for upstream guidance.

The macOS builds are for local development until they have been run on their
target architectures and their GPL corresponding-source obligations are met.
OSXExperts labels its downloads "for educational purposes only"; its page does
not identify a complete matching source bundle. Do not treat the macOS publish
checks as clearance for public redistribution.

## Behavior and tests

BIN audio remains 44.1 kHz, stereo, signed 16-bit PCM. Inputs with another sample
rate/channel count are rejected rather than silently resampled. Encoding keeps
MP3/OGG and the existing bitrate options. Encoded output is decoded and its duration
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
BIN split, MP3 encode, verification, ZIP creation and verification. Both pipelines
use the same managed ZIP engine. GUI, final publication and source deletion are
excluded. The harness measures FFmpeg only, for comparison across builds. Wall-clock measurements include
process startup and disk I/O; this is a practical smoke benchmark, not a codec-only
microbenchmark or a listening-quality assessment. Lossy sample/padding differences
are expected. Real music and longer tracks may behave differently.

Results include one FFmpeg measurement per case, output sizes, hashes and PCM
durations. Compare reports from the same machine, runtime and corpus; timing is
never a unit-test pass/fail gate. Previous original-engine comparisons are historical
and available in Git history, rather than requiring those binaries in today's checkout.
