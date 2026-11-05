# Test structure

KaraokeToolkit.Tests is the shared suite for Core engines, App and CLI behaviour,
including MCP protocol and adapter tests. The product-wide name reflects that scope;
separate project-specific suites can be introduced if dependencies or execution
requirements warrant it. It owns standalone tests, deterministic fixtures and opt-in
benchmarks. Test namespaces match their feature folders. Tests assert explicit
format and workflow expectations using self-contained fixtures.

- Conversion: conversions, input selection, capabilities, file safety and output policies.
- Execution: catalogue completeness, stable persisted IDs, interface registrations and old-schema request/recovery compatibility.
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
- Cli: parsing/settings, unattended policies, output/exit codes, executable processes and adapter integration.
- Mcp: explicit exposure, access policy, safe options, protocol schemas and real stdio client calls.
  Progress ordering is checked directly on stdio before the tool response; SDK client
  notification callbacks may execute concurrently and do not establish wire order.
- Benchmarks: opt-in FFmpeg measurements; never timing assertions in unit tests.
- Support: TestMediaFixture, synchronous progress, artifact paths and controlled test-child entry point.

## Contract coverage

Integration and contract suites exercise the following behaviours:

| Area | Coverage |
| --- | --- |
| BIN conversions and file handling | Conversion/ConversionContractTests: literal sector layout, exact lossless bytes, source deletion/retention, recursion, collisions, invalid inputs, cancellation and empty folders |
| MP3/OGG fidelity | Audio/AudioConversionContractTests: duration, source sample correlation/alignment and unchanged CDG |
| Compressed Audio+G to BIN | Audio/AudioConversionContractTests: every decoded PCM byte and CDG symbol, including sector padding |
| Ten ZIP create/extract scenarios | Archives/BatchZipContractTests: supported audio extensions, member names/payloads, Store/Deflate, recursion, deletion, metadata, overwrite, empty input and cancellation |
| File maintenance | FileOperations/MaintenanceContractTests: filename cleanup, CUE/TXT generation and readable WMA |
| Operation capabilities | Conversion/CapabilityContractTests: all catalog actions, deferred features and unverified optical operations |
| Settings compatibility | Settings/SettingsTests: pinned defaults, previous-schema XML, escaped values, partial/empty entries and accepted boolean representations |
| Native processes and recovery | Conversion callback-failure recovery plus DiscRipping/CdrdaoProcessTests and CdrdaoReaderTests: errors, missing/malformed captures, bounded pipe draining, cancellation/timeout and child reaping |

Ambiguous same-named audio pairs are rejected and left intact rather than silently
choosing one format. Cancellation stops unpublished work while preserving completed
groups. Tests assert these safety contracts explicitly.

Run everything with:

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj
```

Run the standalone contract suites with:

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj --filter FullyQualifiedName~ContractTests
```

Use .NET 10 tooling and the appropriate bundled FFmpeg runtime for integration tests.
Windows-specific Explorer tests still exercise the registry. Disc tests use a controlled
child of this test executable and synthetic media, without requiring an optical drive.
Linux/macOS build preparation needs PowerShell 7; native execution still needs testing
on each OS. A Windows test run does not establish native support on another platform.

Generated fixtures live under `artifacts/tests/runs/<run-id>/fixtures`, and reports under
`artifacts/tests/results`. See [artifacts](artifacts.md). Audio benchmarks run through
this test executable and measure FFmpeg only; see [audio runtime](audio-runtime.md).
