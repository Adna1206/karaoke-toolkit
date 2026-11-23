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

App owns desktop labels and input-picker behaviour. `OperationCommandNames`
defines the preferred invocation names shared by CLI and MCP. Use the desktop
terminology when choosing these names. CLI and MCP requests use the shared
invocation names. Recipes, recovery journals and reports use `OperationId`
names. CLI's `CliOperationCatalog` explicitly includes or excludes each operation.
MCP has its own explicit `McpOperationCatalog` over these identities, with the same
CS8509 compiler guard. Its decisions also apply to recipe/recovery calls. See [MCP](../user/mcp.md).

## Shared processing settings

Register shared settings in Core's `SharedSettingCatalog` with a stable ID,
typed default, validator and explicit CLI and MCP access. Persist the ID in
the `Processing` section of `settings.xml` through `SettingsXml`.
The settings commands and tools use the registry for discovery and persistence.
Desktop presentation preferences stay in App. Resolve the processing profile
when building a request so overrides, portable recipe snapshots and saved
recovery journals retain their established scope.

See [settings format and migrations](settings-format.md) for the profile
format and the rules for changing it.

Bundle release migrations in
[`settings-migrations.json`](../../KaraokeToolkit.Core/Settings/settings-migrations.json).
Entries apply in file order. Keep shipped IDs and values in later releases so
profiles can skip releases safely. Give any new enforcement a new ID. Core
saves edits and applied IDs together in the profile's atomic write. Desktop
commits pending entries at startup. CLI and MCP reads apply them in memory,
and an explicit settings write commits them for the shared profile. A new
profile starts with built-in defaults and records value migration IDs on its
first write. The one-time old profile cleanup runs after the new profile exists
and records its own ID after deletion succeeds.

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

## Rename by Filename Pattern

`RenameByPattern` uses a typed `FilenameRenameRequest` and an approved
`FilenameRenamePlan`. The desktop preview and the recheck before publication
use the same Core planner. It records same-folder renames in the existing
file-move journal. New rename runs start in Desktop. CLI, MCP and recipes have
no direct execution entry point for this operation.
See [Rename by Filename Pattern](../user/file-workflow-features.md#rename-by-filename-pattern).

## Group by Alphabet

Desktop presents one **Group by Alphabet** entry with a scope choice. Its
approved preview is rechecked by Core before it saves a move plan. The
`OrganizeFiles` and `OrganizeFolders` identities continue to describe the
separate scopes. CLI and MCP use `group-by-alphabet-files` and
`group-by-alphabet-folders` as the preferred names.

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
