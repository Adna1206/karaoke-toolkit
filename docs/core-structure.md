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
| Tagging | Filename templates, tag previews, staged metadata writes and packet-payload verification |
| FileOperations | Alphabetical organization, flattening, filename cleanup, CUE/TXT, quarantine and restoration |
| Validation | Read-only integrity checks and findings |
| Recovery | Conversion and recipe/tagging checkpoints, saved move plans and transactional file publication |
| Reporting | Run reports and shared report headers |
| Recipes | Saved workflows, step settings, chaining constraints and execution adapters |
| Settings | Processing preferences and atomic XML settings persistence |
| DiscRipping | cdrdao discovery, disc capture, track extraction and ripping workflow |

ApplicationInfo and AssemblyInfo remain at the root. Related small result and enum
types stay with their feature. OutputConflict and ConflictDecision belong to
Conversion rather than sharing a settings namespace.

Desktop preferences belong to `KaraokeToolkit.App/Features/Settings`: appearance, favourite
operations, expanded sections, tagging-pattern history and settings-window dimensions. The App settings
store composes them with Core's processing settings in the existing XML file,
using a single atomic transaction. Core preserves unknown entries and remains
usable without App or Avalonia. Saved XML names remain unchanged.

Keep serialized setting names, recovery fields and persisted operation values
stable when reorganizing folders. Tests use deterministic media fixtures and
explicit expected outcomes.
