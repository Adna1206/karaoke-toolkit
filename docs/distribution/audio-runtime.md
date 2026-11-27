# Audio runtime and benchmarks

Audio decoding and MP3/OGG/WMA/WAV/FLAC/AAC encoding use FFMpegCore 6.0.0 (MIT),
restored by NuGet.
FFMpegCore invokes bundled FFmpeg/ffprobe processes; NuGet does not supply those
executables. Experimental CD reading uses cdrdao.
See [disc ripping](../user/disc-ripping.md) for packaging and hardware-test limitations.
Tests assert deterministic PCM, graphics, duration and timing expectations.

## Runtime preparation

Directory.Build.targets prepares the native runtime for the selected .NET
Runtime Identifier. Windows x64, Linux x64 and Apple Silicon use Conda-forge
FFmpeg 9.0.2 LGPL builds. The [per-target locks](../../vendor/versions.md#ffmpeg)
pin each package and shipped file by SHA-256. Cold builds fetch the archives into
`artifacts/runtime/downloads/<sha256>/` and extract the pinned FFmpeg, FFprobe,
shared libraries and notices to the build output. Preparation requires tar with
Zstandard support (GNU tar requires the `zstd` executable). Build hosts on Linux
and macOS also require PowerShell 7. The application starts with the bundled
runtime and does not download anything at startup.

Use `./scripts/Prefetch-Runtimes.ps1 -RuntimeIdentifier win-x64,linux-x64,osx-arm64`
to warm the caches. If a pin disappears upstream, restore a copy with the expected
SHA-256 to the corresponding cache directory.

Windows Setup uses Velopack to download and install Microsoft's Visual C++ v14
x64 redistributable when needed. That step requires internet access. Portable
Windows builds and local tests need the runtime installed separately. Windows
supplies the Universal CRT. Unix bundles retain the loader-relative `bin/` and
`lib/` layout. Preparation works without a Conda installation or Conda install
hooks. Cross-preparation verifies archives and layout. Run tests on each native
target before distributing its binary package.

`-p:SkipFFmpegPrepare=true` supports compilation-only checks. The build output
requires a prepared runtime to run media workflows.

## Licensing and distribution

The FFmpeg binaries enable LGPL version 3. Per-package notices and the runtime
lock ship in `ffmpeg/` and `licenses/ffmpeg/`. About reads the latter. The lock
records the version, checksum, provenance and shipped files for each package.

Before distributing a binary release, retain and provide the applicable
corresponding source and build information. See the
[source availability register](source-availability.md) and
[distribution licensing record](distribution-licensing.md#remaining-ffmpeg-work).

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
