# Operation identities and requests

Built-in operation identities live in `KaraokeToolkit.Core/Execution`. The catalogue
covers conversions, validation, tagging/previews, organization, flattening,
restoration, disc reading and deferred actions. App, CLI and recipe adapters use
the same `OperationId` enum rather than declaring independent identities.

| Type | Responsibility |
| --- | --- |
| `Execution/OperationId` | Named identities for built-in operations |
| `Execution/OperationCatalog` | Purpose, conversion routing, folder requirements, unattended support and availability |
| `Execution/OperationCapabilities` | Shared capability queries over the catalogue |
| `Execution/OperationRequest` | General invocation: typed operation identity, inputs and per-run options |
| `Execution/OperationExecutionService` | Unattended adapters to the existing engines |
| `Conversion/ConversionRequest` | Conversion-specific Action/Folder/Options request and validation |
| `ConversionOptions`, `ConversionResult`, `ConversionProgress` | Existing processing contracts, retained in Conversion |

Core does not decide GUI labels, CLI command names or which operations an interface
chooses to expose. App's metadata retains those labels and input-picker behaviour.
CLI's `CliOperationCatalog` explicitly includes each operation with a command name,
or excludes it with a reason. Core still validates execution capabilities independently.
MCP has its own explicit `McpOperationCatalog` over these identities, with the same
CS8509 compiler guard. Its decisions also apply to recipe/recovery calls. See [MCP](../user/mcp.md).

## Adding an operation

1. Add a new `OperationId`; persist its name, never its numeric value.
2. Add its shared requirements and capabilities to `OperationCatalog`.
3. Explicitly expose or exclude it in `CliOperationCatalog`.
4. Add the engine route and relevant interface metadata/setting validation.
   Recipe support is an independent choice in `RecipeOperations`.
5. Extend the relevant contracts and adapter tests.

Core's catalogue and CLI's registration use exhaustive enum switches. Their projects
treat **CS8509 as an error**, so adding an enum entry without accounting for it fails
compilation. There is intentionally no catch-all that could hide a new named entry.
Only the unnamed-numeric-value warning CS8524 is suppressed locally; public lookup
rejects undefined numbers before entering the switches. Do not replace explicit
CLI registrations with generated names or automatically expose new Core operations.

Tests also check registration completeness, unique command names, exclusion reasons,
unattended capability compatibility and shared App/recipe identities.

## Group by Track ID

`GroupByTrackId` uses `OperationRequest.Grouping` and `OutputFolder`. Desktop,
CLI and MCP share the planner. Desktop rechecks the approved plan before
applying. CLI and MCP use the same options and require an explicit output path.
The operation is directly available in all three interfaces. Recipe steps do
not currently include it. See [Group by Track ID](../user/track-grouping.md).

## Persistence and disc ripping

Operation JSON uses names such as `ZipToAudioG`, including nested retry requests.
Numeric tokens and numeric strings are rejected. Enum values and ordering are
internal details, so inserting/reordering members cannot reinterpret saved operations.
Renaming a persisted name is a schema change and needs an explicit decision.
Run and workflow checkpoints use format version 2 with named enum fields (including
status and overwrite policy). Old pre-production journals are rejected without
migration. Reports already use readable operation/status strings.

`DiscRip` is the single physical-disc operation, with BIN, Audio+G or ZIP selected
through `RipFormat`. Duplicate CDG output-mode identities and the duplicate cleanup
identity have been removed. Hardware-unverified status applies to physical capture;
ordinary Audio+G/BIN/ZIP conversion uses the tested processing engines.
See [disc ripping](../user/disc-ripping.md).

App metadata and recipe registrations derive built-in IDs from the enum's names.
Recipe step `OperationId` fields remain strings. User-created recipe instances use
`Recipe:<GUID>` IDs rather than compile-time enum entries. This pre-production
cleanup does not migrate earlier underscored IDs. CLI recipes use the saved definition/settings through Core's recipe runner; dedicated disc commands use typed disc requests.
