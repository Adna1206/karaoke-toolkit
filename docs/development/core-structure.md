# Core structure

`Execution` also contains the read-only `OperationPlanner` and workflow execution
adapters for unattended recipes and explicit quarantine. Destination-folder
conversion requests preserve originals and checkpoint their actual output location.

Core groups related implementation and data types by responsibility. Namespaces
match folders; the app and test tools import those namespaces explicitly.

| Folder / namespace suffix | Responsibility |
| --- | --- |
| Conversion | Conversion coordination, ConversionRequest, options/results, input selection and BIN handling |
| Execution | OperationId, shared catalogue/capabilities, general OperationRequest and unattended execution adapters |
| Archives | Archive readers, ZIP writers, repacking, compression algorithms and levels |
| Audio | Audio encoding/decoding, lossless transcoding and shared FFmpeg runtime discovery |
| Video | Karaoke video rendering and video format settings |
| Naming | Karaoke filename templates and presets, parsed fields and leading track ID recognition |
| Tagging | Mapping filename fields to media tags, tag previews, staged metadata writes and packet-payload verification |
| FileOperations | Alphabetical grouping, flattening, pattern-based renaming, filename cleanup, CUE/TXT, quarantine and restoration |
| Validation | Read-only integrity checks and findings |
| Recovery | Conversion, recipe, tagging and file move checkpoints, saved plans, transactional publication and shared fingerprinting |
| Reporting | Run reports and shared report headers |
| Recipes | Saved workflows, step settings, chaining constraints and execution adapters |
| Settings | Processing preferences and atomic XML settings persistence |
| DiscRipping | cdrdao discovery, disc capture, track extraction and ripping workflow |

Conversion transactions and track grouping copies share `FileStaging` for
copying and fingerprinting the same stream. Each workflow owns its publication
checks and recovery checkpoints.

Archive conversion, tagging, and audio gain use SharpCompress's async readers
and writers with async file streams for staged ZIPs and member files.

Standalone same-volume moves and Track ID grouping copies share
`MoveCheckpointLog`. The workflow JSON holds the initial plan and the final sealed
result. The checksummed log records group starts and receipts between those
snapshots. Copies also flush the verified staging fingerprints before publication.
Standalone move and grouping copy recovery require this journal format.
Standalone conversions use the same `CheckpointLog<T>` framing to record batch
progress. File transactions keep their own publication journal and can reuse
fingerprints within the final commit checks.
Approved and resumed moves share ancestor checks within a group and recheck
paths for each new group. Publication still uses a rename that refuses to
overwrite an occupied destination.

ApplicationInfo and AssemblyInfo remain at the root. Related small result and enum
types stay with their feature. OutputConflict and ConflictDecision belong to
Conversion rather than sharing a settings namespace.

`KaraokeFilenamePattern` parses stems using a custom template or an explicit
preset. `DiscIdArtistTitle` keeps the whole disc identifier, and
`DiscIdTrackArtistTitle` captures the track digits separately. Tagging keeps
the latter as its default and converts the captured digits to a numeric tag.
Pattern-based renaming uses the same presets and preserves the captured track
digits when formatting the new stem.
Track grouping and separator edits share leading ID recognition. Each operation
applies its own separator rules, and grouping owns folder masks and ranges.

Desktop preferences belong to `KaraokeToolkit.App/Features/Settings`: appearance, favourite
operations, expanded sections, tagging-pattern history and settings-window dimensions. The App settings
store composes them with Core's processing settings in versioned `settings.xml`,
using a single atomic transaction. Readers ignore unknown keys and use defaults
for unsupported saved values. Both Core and Desktop saves preserve unknown
entries in supported profiles. Core remains usable without App or Avalonia.
See [settings format and migrations](settings-format.md).

Keep serialized setting names, recovery fields and persisted operation values
stable when reorganizing folders. Tests use deterministic media fixtures and
explicit expected outcomes.
