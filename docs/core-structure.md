# Core structure

Core groups related implementation and data types by responsibility. Namespaces
match folders; the app and test tools import those namespaces explicitly.

| Folder / namespace suffix | Responsibility |
| --- | --- |
| Conversion | Conversion coordination, options, requests, results, input selection and BIN handling |
| Archives | Archive readers, ZIP writers, repacking, compression algorithms and levels |
| Audio | Audio encoding/decoding, lossless transcoding and shared FFmpeg runtime discovery |
| Video | Karaoke video rendering and video format settings |
| FileOperations | Filename cleanup, CUE/TXT creation, quarantine and restoration |
| Validation | Read-only integrity checks and findings |
| Recovery | Run journals, interrupted-run recovery and transactional file publication |
| Reporting | Run reports and shared report headers |
| Settings | Processing preferences and atomic XML settings persistence |
| DiscRipping | cdrdao discovery, disc capture, track extraction and ripping workflow |

ApplicationInfo and AssemblyInfo remain at the root. Related small result and enum
types stay with their feature. OutputConflict and ConflictDecision belong to
Conversion rather than sharing a settings namespace.

Desktop preferences belong to `KaraokeToolkit.App/Features/Settings`: appearance, favourite
operations, expanded sections and settings-window dimensions. The App settings
store composes them with Core's processing settings in the existing XML file,
using a single atomic transaction. Core preserves unknown entries and remains
usable without App or Avalonia. Saved XML names remain unchanged.

This organization does not introduce new projects or change serialized setting
names, recovery fields, or persisted operation values. Tests use deterministic
media fixtures and explicit expectations; no original-engine adapters are retained.
