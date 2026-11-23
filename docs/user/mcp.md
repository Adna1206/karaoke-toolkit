# Local MCP integration

`KaraokeToolkit.Mcp` is a .NET 10 console project using the official MCP C# SDK
([official C# SDK](https://github.com/modelcontextprotocol/csharp-sdk),
`ModelContextProtocol` 2.4.0). It references Core directly; it never runs CLI
commands, starts the GUI, listens on a port or installs a background service.
An MCP client launches `karaoke-toolkit-mcp` when needed and exchanges JSON-RPC
messages through stdin/stdout. Diagnostic logs go to stderr only.

## Client setup

The executable ships beside App and CLI in the normal package. In development,
build the solution and point the client at the MCP output executable (or use
`dotnet` with its DLL). Use the path for the actual installed payload, including
Velopack's `current` directory when applicable. Reconfigure paths if the location
changes. Settings → Integrations → MCP shows the installed executable path and
provides Copy Path and Copy Client Configuration actions. CLI and MCP appear as
separate collapsible sections on one scrolling page, both initially expanded.
MCP's Client configuration panel contains its file-access dropdown, configuration
text and Copy Client Configuration action together.

**File access for copied configuration** defaults to **Read only**. Choosing
**Allow file changes** adds `--allow-writes` to the generated example; copy it into your MCP client's
configuration and reconnect that client. The choice is not persisted and does not
change an already-running process, edit client files or save a global permission.
Some clients use a different JSON format; use the same executable and arguments.

Generic client configuration, read-only by default:

```json
{
  "mcpServers": {
    "karaoke-toolkit": {
      "command": "C:\\Path\\To\\KaraokeToolkit\\karaoke-toolkit-mcp.exe",
      "args": []
    }
  }
}
```

For Linux/macOS, use the corresponding absolute executable path with no `.exe`.
To permit file-changing tools, the client owner explicitly adds `--allow-writes`
to `args`. Each changing call must also pass `confirmFileChanges: true` after the
client obtains user authorization. A model-supplied tool argument cannot override
a process launched without that startup permission.
This per-call flag records intent for authorized work; it does not require a
separate popup for every file. Read-only startup prevents conversions and other
file changes, while discovery, checks and previews remain available.

There is no enable switch or overhead from an MCP process when no client launches
it. `--help` describes startup flags and `--version` returns the shared app version;
normal stdio startup prints no human-readable banner to stdout. The MCP SDK performs
protocol initialization/tool discovery and propagates cancellation notifications.

## Tools

| Tool | Purpose |
| --- | --- |
| `get_operations` | Shared CLI and MCP names, Core IDs, availability and file-change requirements |
| `get_settings` | Per-call safe defaults and named encoding/compression/video choices |
| `list_settings`, `get_setting` | Effective shared processing preferences, their types and available values |
| `set_setting` | Update a shared preference by ID with owner-enabled writes and per-call confirmation |
| `validate_karaoke_files` | Read-only structural, pairing and ZIP integrity checks |
| `preview_tags` | Existing and filename-derived metadata, without writing |
| `plan_operation` | Read-only scope, candidate outputs and detectable conflicts |
| `run_operation` | Included conversions, tagging, file tools, quarantine/restoration |
| `run_recipe` | Portable recipe object with saved per-step settings |
| `list_recovery` | Available conversion, tagging, file move and recipe journals, without resuming |
| `recover` | Recover an interrupted item and continue the saved batch if enabled, or resume a recipe snapshot |

Results contain Core's completion report, found count, issue flag and optional tag
preview. Structured results also have a JSON text fallback for clients. Reports
are returned to the client; no arbitrary report file is written. Failures are MCP
tool errors with actionable messages. Processing failures already represented in
Core reports remain in those reports (`hasIssues`), so inspect both protocol errors
and the result. Cancellation reaches Core; completed groups stay committed.
Progress notifications use the client's progress token and a nondecreasing 0-100
scale. This process serializes processing calls to avoid overlapping mutations.

## Requests and settings

Example arguments for `plan_operation` (and `run_operation` with additional
`confirmFileChanges: true`):

```json
{
  "request": {
    "operationId": "zip-to-audiog",
    "inputs": ["D:\\Karaoke\\song.zip"],
    "overwritePolicy": "Fail",
    "options": {
      "audioEncoder": "MP3",
      "audioBitrate": 320,
      "autoCreateCueText": false,
      "deleteOldFiles": false,
      "zipAlgorithm": "Deflate",
      "zipCompression": "5"
    }
  }
}
```

Set `operationId` to the `name` shown by `get_operations`. Recipe step
definitions use the separate `coreId` values. MCP options accept camelCase or
PascalCase property names, rejecting unknown/duplicate settings. Non-ZIP enums
use names. ZIP levels accept current levels 0-9 (numbers/strings) or current enum
names; `Balanced` means 5. Omitted deletion preferences **always keep originals**,
even when an options object is supplied. Flags/options files from CLI are not used.

Omitted processing values inherit the shared per-user profile. A saved **Ask**
overwrite preference uses **Fail** for unattended runs. `set_setting` takes an
ID and scalar value, for example `recovery.verification` and `XxHash128`. It
requires `--allow-writes` and `confirmFileChanges: true`. For media conversions,
`outputFolder` follows Core's
separate-destination constraints: keep originals, destination outside selected
input folders, collision protection, and `--no-cue-text`'s equivalent
`autoCreateCueText: false` when multiple source folders share a listing destination.
Recipe steps use their supplied snapshots rather than global settings. All steps
must be included in MCP and use Fail, Skip or Replace, never Ask. Imported recipes
and file metadata are data, not authorization or instructions for the client.

Plans are snapshots, not integrity/encoding guarantees. Tag plans use read-only tag
preview. Recipe execution has no speculative whole-pipeline preview tool; later
scopes depend on real preceding outputs. GUI/CLI workflows remain available for
recipe planning. No shell/script execution, physical-disc capture, player, online
lookup or enabled updater is exposed by MCP.

### Recovery settings for new runs

For standalone media conversions, grouping, alphabetical organization, and flattening, `recoveryVerification`
defaults to `XxHash128`. You can also set it to `Sha256`, `MetadataOnly`, or
`None`. Use `checkTiming` with `BeforeBatch` (more robust) or `BeforeItem`
(default, faster to start) to override `recovery.check-timing` for a standalone media
conversion, file move or grouping request. For those operations and tagging,
`saveResumableRun: false` overrides `recovery.batch-resume` for the request.
`list_recovery` shows whether a saved job continues the batch after recovery.
Set `recovery.check-on-startup` to control Desktop startup discovery. MCP
discovery remains explicit through `list_recovery`. See [recovery settings](crash-recovery.md#recovery-settings).

### Track ID separator

Pass `add-track-id-separator` or `remove-track-id-separator` as the request's
`operationId`, with source folders in `inputs`. Use `plan_operation` to
preview and `run_operation` with `confirmFileChanges: true` on a process
launched with `--allow-writes` to apply.

### Group by Track ID

Use `plan_operation` to inspect a grouping and `run_operation` to apply it. Both
accept the same `request` payload. Applying a copy or move also requires
`confirmFileChanges: true` on a process launched with `--allow-writes`.

```json
{
  "request": {
    "operationId": "group-by-track-id",
    "inputs": ["D:\\Incoming\\ABC-31xxx", "D:\\Incoming\\ABC-32xxx"],
    "outputFolder": "D:\\Grouped",
    "grouping": {
      "wholeFolders": true,
      "trailingDigits": 4,
      "mask": "x"
    }
  }
}
```

This copies each folder intact under `ABC-3xxxx`. Omit `wholeFolders` to scan
selected folders for files. Set `recurse: true` to scan subfolders in file mode.
Selected files can be used in either mode. For numeric ranges, set `rangeSize` and
optionally `rangeStart` (default 1) or `rangeSeparator` (default `" - "`).
Trailing-digit grouping is the default with two masked digits. Its optional
`createParents`, `maxParentLevels` and `skipSingleChildParents` settings control
automatic nesting. `idSeparator` defaults to a dash and accepts an empty string.
Set `move: true` for a same-volume move. Conflicts block execution.

### Group by Alphabet

Use `group-by-alphabet-files` for files or `group-by-alphabet-folders` for
immediate subfolders. Both names match the CLI. Use `plan_operation` to
review destinations before applying the move with `run_operation`.

Use Desktop for **Rename by Filename Pattern** previews and runs.

### Normalize Audio Gain

Pass the following arguments to `plan_operation` to measure audio and receive
`audioGainPreview` rows. Use the same request with `run_operation` on an
owner-enabled `--allow-writes` server and add `"confirmFileChanges": true`
after the user authorizes writing copies.

```json
{
  "request": {
    "operationId": "normalize-audio-gain",
    "inputs": ["D:\\Karaoke"],
    "outputFolder": "D:\\Adjusted",
    "audioGain": {
      "mode": "Loudness",
      "targetLufs": -16,
      "peakCeilingDbtp": -1
    },
    "options": { "recurseFolders": true }
  }
}
```

`audioGain.mode` accepts `Loudness`, `Peak` or `Offset`. Peak uses
`peakCeilingDbtp` as the target. Offset uses the signed `offsetDb` adjustment.
The output folder must already exist and be separate from the selected source
folders. `overwritePolicy` defaults to `Fail`. Only output copies can be
replaced. Inputs can be loose audio files, single-track karaoke ZIPs, or
folders. See [Normalize Audio Gain](audio-gain.md).

### Change Audio Key

Send a `plan_operation` request with `operationId` set to `change-audio-key`,
the local `inputs`, an existing `outputFolder`, and
`"audioKey": { "semitones": -2 }`. The response
includes `audioKeyPreview` rows. The same request works with `run_operation`
when the server starts with `--allow-writes` and the user authorizes the
call with `confirmFileChanges: true`. See [Change Audio Key](audio-gain.md#change-audio-key).

## Exposure and permissions

`McpOperationCatalog` has one explicit include/exclude switch arm for every
`OperationId`. CS8509 is an error in the MCP project; there is no catch-all or
automatic exposure. New operations must get an explicit MCP decision independently
of CLI and App. An omission fails the build with CS8509, naming the missing enum
member and catalogue source file; a failed build cannot start an MCP process. Exposure checks also apply to recipe steps and saved recovery
identities, so those routes cannot bypass excluded operations. Unknown runtime names
produce an MCP tool error identifying the unregistered operation and directing the
client to `get_operations`. Deliberately excluded IDs include their exclusion reason.

Read-only startup is an application policy, not an OS filesystem sandbox: allowed
read tools can inspect paths readable by the current user, and write-enabled tools
have that user's filesystem rights. There is no directory allowlist or automatic
elevation. The client remains responsible for user approval; `confirmFileChanges`
records caller intent, not a cryptographic proof of consent. Run under an appropriately
restricted account if OS-level boundaries are needed. Core's path, link, hash,
overwrite, transaction and recovery protections remain active.

## Tests and publishing

`KaraokeToolkit.Tests/Mcp` covers explicit catalog decisions, access gates, settings
parsing, progress, cancellation, excluded recovery identities and real stdio SDK
client initialization/discovery/checks/conversion/recipes/recovery. Core retains
media-processing tests. Native platform CI includes MCP alongside CLI; local Windows
results do not establish Linux/macOS verification.

`scripts/Publish.ps1` publishes App, CLI and MCP into the same per-RID payload and
checks MCP files/notices before packaging. Shared logging/DI abstractions are aligned
at 11.0.0 to prevent incompatible DLL copies in that combined folder. MCP has no
Avalonia/Velopack/CLI project dependency and adds no SDK dependency to App startup.
See [publishing](../distribution/publishing.md), [operations](../development/operations.md) and
[licence inventory](../../licenses/README.md).
