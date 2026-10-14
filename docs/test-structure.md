# Test structure

KaraokeToolkit.Core.Tests owns standalone tests, deterministic fixtures and opt-in
benchmarks. Test namespaces match their feature folders. No MP3+G Toolz binaries,
BASS/DynaZip libraries, x86 bridge, or .NET Framework compiler/runtime is needed.

- Conversion: conversions, input selection, capabilities, file safety and output policies.
- Archives: compression, extraction, batch ZIP contracts and compatibility repacking.
- Audio: FFmpeg encoding/decoding, sample fidelity, timing and lossless transcoding.
- Video: karaoke video encoding and synchronization.
- Tagging: custom patterns, numeric tracks, media/tag readback, decoded-audio fidelity, ZIP member names and graphics, stale previews, cleanup controls and interrupted publication.
- FileOperations: organization, flattening, cleanup, CUE/TXT, quarantine and restoration.
- Validation: structural checks, pairing, CRCs and excluded folders.
- Recovery: interrupted conversions/recipes/tagging, process termination, frozen move plans and file transactions.
- Reporting: report contents and consistent headers.
- Settings: persistence, defaults, validation and atomic saves.
- DiscRipping: synthetic images, fake drives, subchannels and native-tool process handling.
- Recipes: chaining, settings snapshots and workflow execution.
- App: screen/view-model tests grouped by their App feature or shared responsibility.
- Benchmarks: opt-in FFmpeg measurements; never timing assertions in unit tests.
- Support: TestMediaFixture, synchronous progress, artifact paths and controlled test-child entry point.

## Replacement coverage

The former comparison suites now assert expected outcomes directly:

| Former comparison | Standalone coverage |
| --- | --- |
| BIN conversions and 29 file-handling/error cases | Conversion/ConversionContractTests: literal sector layout, exact lossless bytes, source deletion/retention, recursion, collisions, invalid inputs, cancellation and empty folders |
| Four decoded MP3/OGG bitrate comparisons | Audio/AudioConversionContractTests: duration, source sample correlation/alignment and unchanged CDG |
| Compressed Audio+G to BIN | Audio/AudioConversionContractTests: every decoded PCM byte and CDG symbol, including sector padding |
| Ten ZIP create/extract scenarios | Archives/BatchZipContractTests: supported audio extensions, member names/payloads, Store/Deflate, recursion, deletion, metadata, overwrite, empty input and cancellation |
| Original cleanup, CUE and WMA quirks | FileOperations/MaintenanceContractTests: actual cleanup, CUE/TXT generation and readable WMA. Current services already corrected the original failures/no-op; tests do not preserve obsolete adapter bugs. |
| Original dispatch capabilities | Conversion/CapabilityContractTests: all catalog actions, deferred features and unverified optical operations |
| Settings defaults and XML interoperability | Settings/SettingsTests: pinned defaults, handwritten previous-schema XML, escaped values, partial/empty entries and VB boolean representations |
| Original bridge/process protocol | Conversion callback-failure recovery plus DiscRipping/CdrdaoProcessTests and CdrdaoReaderTests: errors, missing/malformed captures, bounded pipe draining, cancellation/timeout and child reaping |

Ambiguous same-named audio pairs are rejected and left intact rather than silently
choosing one format. Cancellation stops unpublished work; it is no longer compared
against an original engine which sometimes ignored it. Tests assert these current
safety contracts explicitly. The retired JSON bridge protocol has no production
consumer, so its parser-specific tests and copied parser were removed together.

Run everything with:

```powershell
dotnet test KaraokeToolkit.Core.Tests/KaraokeToolkit.Core.Tests.csproj
```

Run the standalone contract suites with:

```powershell
dotnet test KaraokeToolkit.Core.Tests/KaraokeToolkit.Core.Tests.csproj --filter FullyQualifiedName~ContractTests
```

Use .NET 10 tooling and the appropriate bundled FFmpeg runtime for integration tests.
Windows-specific Explorer tests still exercise the registry. Disc tests use a controlled
child of this test executable and synthetic media, without requiring an optical drive.
Linux/macOS build preparation needs PowerShell 7; native execution still needs testing
on each OS. Removing the old bridge does not claim that those platform checks are done.

Generated fixtures live under `artifacts/tests/runs/<run-id>/fixtures`, and reports under
`artifacts/tests/results`. See [artifacts](artifacts.md). Audio benchmarks run through
this test executable and measure FFmpeg only; see [audio runtime](audio-runtime.md).
