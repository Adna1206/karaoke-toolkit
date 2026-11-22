# Command-line interface

`KaraokeToolkit.Cli` is a .NET 10 console project referencing Core, with no Avalonia
or Velopack runtime dependency. The executable is `karaoke-toolkit` (`.exe` on
Windows). It reads the shared per-user processing profile. Use `settings set`
to update a saved preference. Recipes, explicit quarantine and experimental physical
disc capture use the existing Core services.

## Running

From the repository root:

```powershell
dotnet run --project KaraokeToolkit.Cli -- --help
dotnet run --project KaraokeToolkit.Cli -- operations
dotnet run --project KaraokeToolkit.Cli -- validate "D:\Karaoke" --json --quiet
```

Builds prepare the appropriate bundled FFmpeg runtime. `scripts/Publish.ps1`
includes the CLI beside the desktop executable in Windows/Linux packages, sharing
the runtime, Core and native tools. Run it by full path or from its containing
directory; the installer does not add it to PATH. In Velopack portable ZIPs,
the actual executables are inside `current/`.

The Linux AppImage's normal entry point is the GUI. To use the bundled CLI,
extract the AppImage (`--appimage-extract`) and run `karaoke-toolkit` from its
extracted `usr/bin` directory. CLI ships with App rather than as a separate product.

Close CLI operations before manually installing/updating the application.
The desktop app handles update checks. Linux packages require native runtime testing.

## Commands and settings

For complete workflows, see [CLI examples](cli-examples.md). Run `--help` for the
flag reference and `operations --json` for machine-readable discovery.

```powershell
karaoke-toolkit settings list
karaoke-toolkit settings get recovery.verification
karaoke-toolkit settings set recovery.verification XxHash128
```

For new runs, command flags override an explicit options file, which overrides
the shared profile. Unattended conversions keep originals unless explicitly
requested per run. A saved **Ask** overwrite preference uses **Fail** in
unattended runs. Recipes and recovery continue to use their own saved settings.

`operations` lists supported names and Core IDs. Use `run NAME` for conversions,
archive tools, folder tools and manifest restoration. Stable IDs such as
`ZipToAudioG` are accepted too. `validate`, `tag` and `tag-preview` have direct
shortcuts. Use positional paths, repeated `--input PATH`, or both. Quote paths with
spaces; use `--` before positional paths that begin with a dash.

```powershell
.\karaoke-toolkit.exe run zip-to-audiog "D:\Incoming" --no-cue-text
.\karaoke-toolkit.exe run audiog-to-zip --input "D:\Disc One" --input "D:\Disc Two" --zip-level 5
.\karaoke-toolkit.exe run repack-zip "D:\Incoming" --zip-algorithm Deflate --delete-originals
.\karaoke-toolkit.exe run organize-files "D:\Karaoke" --ignore-leading-the
.\karaoke-toolkit.exe run flatten-one-layer "D:\Incoming"
.\karaoke-toolkit.exe tag-preview "D:\Karaoke" --pattern "%ARTIST% - %TITLE%" --json --quiet
.\karaoke-toolkit.exe tag "D:\Karaoke" --pattern "%ARTIST% - %TITLE%" --fill-empty-only
```

New conversion runs **keep originals** and **fail on existing outputs** by default.
Choose `--delete-originals` explicitly to request source deletion, and
`--overwrite skip|replace` when wanted. `Ask` is rejected: no CLI command waits for
a GUI prompt. Tagging updates metadata in place without re-encoding, regardless
of delete/overwrite preferences. Tagging pattern/media errors are reported as
failures; BIN and orphan CDG inputs are skipped because they have no tag fields.
`tag-preview` is read-only and prints parsed plus existing metadata in JSON output.
Tagging takes a fresh fingerprinted preview when it runs.

Commands and named enum choices ignore case. Option names and JSON property names
are case-sensitive. Named enum settings reject numeric values, except the explicit
ZIP compression level (0-9). `--option=value` and `--option value` both work.
Only `--input` can repeat; contradictory flags such as `--recurse --no-recurse`
are rejected.

`--recurse` is the default for conversions. Group by Track ID scans immediate files
unless `--recurse` is supplied. Organize Files always recurses; Organize Folders acts on
immediate child folders; flatten depth is selected by the operation. These folder
operations keep their established scope regardless of the recursion preference.

`--help` documents encoding, video, compression, validation and tag cleanup flags.
`--options FILE.json` supplies typed `ConversionOptions`; explicit command flags
override the file. Unknown/repeated properties are rejected. Omitted deletion
preferences still default to keeping originals. ZIP compression accepts 0-9 as a
number, numeric string or current enum name (`Balanced` means level 5).

Defaults are MP3 at 320 kbps, Deflate at level 5, recursion and CUE/TXT generation
enabled where applicable. Optional input validation is off; the engines retain
their required output checks. Shared flags apply only where the engine uses them:
extracting a ZIP does not re-encode its audio. Video flags require
`run karaoke-to-video`; `--ignore-leading-the` requires `run organize-files`;
tagging flags require `tag` or `tag-preview` (or their `run` equivalents).

Video defaults are MP4, HD (1280x720), no smoothing, 30 fps and 2000 kbps video.
Audio matches the detected source bitrate with a 192 kbps fallback.
`--video-audio-bitrate` changes that fallback; `--video-audio-fallback-only` uses
the selected rate unconditionally. Resolution names are SD (640x480), HD (720p),
FullHD (1080p), QHD (1440p) and UHD (2160p).

```json
{
  "AudioEncoder": "MP3",
  "AudioBitrate": 192,
  "ZipAlgorithm": "Deflate",
  "ZipCompression": 5,
  "RecurseFolders": false,
  "AutoCreateCueText": false,
  "DeleteOldFiles": false
}
```

The options file covers all `ConversionOptions`, including settings without a
dedicated flag, such as `AutoRenameZip`, `RenameBins` and `NamingFormat`.
Disc-capture fields are not useful here because ripping is excluded. Request
settings such as overwrite policy, input validation, tagging pattern/cleanup and
the leading-The rule are separate flags, not fields in this JSON file.

Operations write beside sources by default. Media conversions accept `--output FOLDER`
for an existing destination outside selected input folders. This preserves originals;
`--delete-originals` is rejected with `--output`. Outputs are flattened into the
destination, with conflicts checked before processing and again at publication.
Multiple source folders sharing a destination require `--no-cue-text` for conversions
that would generate listings; create a single listing there afterward. Among the folder
tools, Group by Track ID takes a separate destination. Options files provide reusable
conversion preferences without loading GUI settings.

## Output and exit codes

Progress goes to stderr. `--quiet` suppresses progress, not results. `--json` writes
one JSON document to stdout; operation results include `ExitCode`, `Found`,
`HasIssues`, the standard `Report`, and optional `TagPreview`. Error responses carry
`ExitCode` and `Error`. Unknown operations identify the unregistered name and point
to `karaoke-toolkit operations`; deliberately excluded IDs explain their reason.
These usage errors exit with code 2. Discovery commands return JSON arrays. `--version` prints
the shared app version.

`--report NEW.json|NEW.txt` saves the standard completion report, with the shared
application/version/date header. The parent directory must exist. Existing reports
are refused before processing; file creation also refuses a race with another writer.
A report write failure can occur after processing completed; inspect its error
before retrying a file-changing operation.

| Exit | Meaning |
| --- | --- |
| 0 | Completed without failures (explicit skips are allowed) |
| 1 | Processing error, invalid media, validation findings or failed tag preview |
| 2 | Invalid command, arguments or settings |
| 3 | No matching items found |
| 130 | Cancelled |

Ctrl+C requests cancellation through Core. Completed groups remain committed;
unpublished work follows the engine's transaction/cleanup rules. Validation is
read-only and does not automatically quarantine files.

## Recovery

```powershell
.\karaoke-toolkit.exe recovery-list "D:\Incoming" --json
.\karaoke-toolkit.exe recover "D:\Incoming\.karaoke-job-run-ID\run.json" --quiet
```

Without folders, `recovery-list` uses the app's startup recovery index. With folders,
it also discovers journals there. `recover` takes an available journal from that
list, retaining the saved processing/deletion/overwrite settings. Saved `Ask`
conflicts cannot prompt and will fail. Recipe recovery uses its saved definition/settings even if the recipe file was deleted. Damaged journals and changed files remain
protected. See [crash recovery](crash-recovery.md).

## Recipes, previews and quarantine

`recipe list FOLDER`, `recipe show FILE`, `recipe validate FILE` and
`recipe run FILE PATH...` work on the existing portable recipe JSON format.
Each step uses its saved settings. CLI processing overrides are rejected for recipes;
all steps must use Fail, Skip or Replace, never Ask. Recipe validation checks the
whole known pipeline before any processing. `recover JOURNAL` also resumes recipe
snapshots, interrupted steps and remaining steps through Core's existing recovery.
No script runner or recipe editing UI is introduced.

`--dry-run` plans file conversions, folder moves/renames, track grouping, restoration and quarantine
without changing inputs or creating outputs/journals. Plans list candidate targets
and detectable collisions, not guarantees that content will encode successfully.
`tag --dry-run` uses tagging preview; `validate --dry-run` remains a read-only check.
Recipe dry runs inspect the first step and list later steps as deferred: their
actual scope depends on outputs that do not exist yet. Disc dry runs scan the disc
and report tracks, without reading its full image or publishing tracks.
`--report` can explicitly save the dry-run report. Execution repeats its safety checks;
files may change after a plan was printed.

`quarantine PATH...` (also `run quarantine-invalid PATH...`) validates inputs and
moves only eligible failures into Damaged with restoration manifests. Valid files
remain in place; unreadable/nonmovable findings are reported as issues. Use
`--dry-run` first, and `run restore-quarantined-files MANIFEST` to restore a group.
Quarantine is never an automatic side effect of `validate`.

## File move recovery settings

Grouping, alphabetical organization and flattening default to xxHash128 and accept
`--recovery-verification sha256|xxhash128|metadata|none` and
`--batch-resume` or `--no-batch-resume`. See [recovery verification](crash-recovery.md#verification-settings)
for defaults and how interrupted runs are handled.

## Track ID separator

Run `karaoke-toolkit run add-track-id-separator <folder>` to change
`ABC123 - Artist - Title` to `ABC-123 - Artist - Title`. Use
`remove-track-id-separator` for the reverse. Both work in place on supported
karaoke files and preserve the rest of each name. Use `--dry-run` to preview.

## Group by Track ID

`run group-by-track-id PATH... --output FOLDER` accepts files and folders.
Supply the destination explicitly. It can be the folder being scanned. Copy is the default.
`--move` requires sources and destination on the same volume. Conflicts block
the run. Use `--dry-run` to inspect the paths and issues first.

```powershell
.\karaoke-toolkit.exe run group-by-track-id "D:\Incoming" --output "D:\Grouped" --recurse --range-size 50 --range-start 1 --range-separator " to " --dry-run
.\karaoke-toolkit.exe run group-by-track-id "D:\Incoming\ABC-31xxx" "D:\Incoming\ABC-32xxx" --output "D:\Grouped" --whole-folders --trailing-digits 4 --dry-run
```

The first example uses names such as `ABC-33601 to ABC-33650`. The second groups
existing batch folders intact under `ABC-3xxxx`. Trailing-digit grouping defaults to
two digits, with `--mask x|X|#|s` to choose the name style. Use
`--id-separator=` to omit the dash after the prefix. `--create-parents` builds
broader trailing-digit groups automatically. `--max-parent-levels N` limits the
depth (0 means all). `--keep-single-child-parents` preserves levels that would
otherwise be collapsed.

## Disc commands

Use `disc-list [--cdrdao EXECUTABLE] [--json]` to discover exact device addresses.
`disc-rip --device DEVICE --output FOLDER --disc-name NAME --experimental`
accepts `--tracks all|1,2,4-6`, `--format Bin|AudioG|Zip`, `--cdrdao EXECUTABLE`,
normal encoding/compression/CUE preferences, overwrite policy and output/report flags.
Use `disc-rip --help` for the command's syntax; `run disc-rip` is deliberately rejected.
BIN is the default output. Capture uses Core's cdrdao reader and track publication,
including disc identity checks, cancellation and verified output transactions.
Windows cdrdao is bundled with the app/CLI package. Linux/macOS resolve an
external installation or explicit executable path. CLI never requests elevation.
Physical drive/subchannel compatibility still requires hardware testing.

## Help and shell integration

In the desktop app, **Settings → Integrations → Command Line** shows the packaged
CLI path and availability, Copy Path, Open Terminal Here, and copyable examples for
PowerShell/Bash/Zsh. The shell selector changes example syntax; the terminal button
opens the platform's terminal in the app folder without running an operation.
The panel also supplies opt-in completion/PATH setup instructions. Its copy/launch
actions are immediate and do not need Apply. Development builds with no CLI beside
App explain that the published package includes it. No processing preferences,
shell profiles or PATH values are changed, and no CLI background service is started.
The [MCP executable](mcp.md) is available separately from this panel; its setup UI
remains deferred with the Settings redesign.
The compact layout keeps the executable and terminal action together, uses one shell
selector for all copied commands, and initially collapses Command examples and Shell
setup. Brief hints appear only when relevant; longer explanations live in tooltips.

`help OPERATION` and `run OPERATION --help` show operation-specific guidance.
`help recipe`, `help disc-rip` and other dedicated-command topics show their syntax.
`operations --json` identifies dedicated commands and hardware-unverified support.

`completion powershell|bash|zsh` prints completion registration for commands and flags;
it does not edit a profile. File completion remains handled by the shell. It provides
word completion rather than full context-sensitive enum-value completion.
`path powershell|bash|zsh` prints opt-in PATH setup for the current installation.
Evaluate it for the current shell or save it in your profile yourself. Neither
command modifies the environment or profile automatically. Update the path after
moving the installation or switching a versioned Velopack payload directory.

## Platform verification

The local test suite runs on Windows. `.github/workflows/cli-platforms.yml` is a
read-only-permission, manually runnable/PR workflow for Windows, Linux and macOS ARM64.
It runs CLI/shared-contract tests and native FFmpeg video fixtures,
smoke-tests the built CLI executable, and retains TRX reports. App and CLI continue
to be published together by the existing publish script. Non-Windows jobs also check completion script syntax where shells exist.
The workflow must run on those hosts before claiming Linux/macOS verification;
adding it is not evidence that those runs passed. macOS installer packaging remains
separate future work. Tests use fake disc readers rather than physical drives.

## Architecture and tests

Core's `OperationId` and `OperationCatalog` declare identities and capabilities.
`CliOperationCatalog` explicitly exposes or excludes every enum entry; a new entry
without a decision fails compilation. Command names, parsing, formatting and exit
codes stay in CLI. General requests use the enum; conversion engines receive a
separate `ConversionRequest`. See [operation contracts](../development/operations.md).
`OperationExecutionService` calls existing engines and can be reused directly by
the MCP adapter, without launching CLI subprocesses. The desktop routes remain
unchanged.

`KaraokeToolkit.Tests/Cli` tests argument/settings mapping, safe defaults, JSON,
exit codes, report protection, real extraction/tagging/recovery and the executable
entry point. Core's existing suites retain detailed conversion coverage.
