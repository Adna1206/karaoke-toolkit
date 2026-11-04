# Command-line interface

`KaraokeToolkit.Cli` is a .NET 10 console project referencing Core, with no Avalonia
or Velopack runtime dependency. The executable is `karaoke-toolkit` (`.exe` on
Windows). It does not launch the GUI, run in the background, load desktop preferences
or modify saved app settings. Recipes and physical disc capture are not exposed.

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

The Linux AppImage's normal entry point is the GUI. For CLI-only Linux use, publish
the console project directly, or extract the AppImage (`--appimage-extract`) and
run `karaoke-toolkit` from its extracted `usr/bin` directory.

The CLI can also be published on its own:

```powershell
dotnet publish KaraokeToolkit.Cli/KaraokeToolkit.Cli.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/cli/win-x64
dotnet publish KaraokeToolkit.Cli/KaraokeToolkit.Cli.csproj -c Release -r linux-x64 --self-contained true -o artifacts/publish/cli/linux-x64
```

Close CLI operations before manually installing/updating the application. Packaging
does not enable automatic update checks or imply native Linux runtime testing.

## Commands and settings

For complete workflows, see [CLI examples](cli-examples.md). Run `--help` for the
flag reference and `operations --json` for machine-readable discovery.

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

`--recurse` is the default. Organize Files always recurses; Organize Folders acts on
immediate child folders; flatten depth is selected by the operation. These folder
operations keep their established scope regardless of the recursion preference.

`--help` documents encoding, video, compression, validation and tag cleanup flags.
`--options FILE.json` supplies typed `ConversionOptions`; explicit command flags
override the file. Unknown/repeated properties are rejected. Omitted deletion
preferences still default to keeping originals. ZIP compression accepts 0-9 as a
number, numeric string or current enum name (`Balanced` means level 5).

Defaults are MP3 at 128 kbps, Deflate at level 5, recursion and CUE/TXT generation
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

Operations normally write beside their source files; folder tools use the selected
roots. There is no general `--output` destination option or persistent CLI profile.
Use an options file to reuse conversion settings without loading GUI preferences.

## Output and exit codes

Progress goes to stderr. `--quiet` suppresses progress, not results. `--json` writes
one JSON document to stdout; operation results include `ExitCode`, `Found`,
`HasIssues`, the standard `Report`, and optional `TagPreview`. Error responses carry
`ExitCode` and `Error`. Discovery commands return JSON arrays. `--version` prints
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
conflicts cannot prompt and will fail. Recipe recovery is explicitly rejected;
use the desktop application for recipes. Damaged journals and changed files remain
protected. See [crash recovery](crash-recovery.md).

## Current boundaries

- Recipes and recipe recovery remain excluded by agreement. Individual operations,
  conversion recovery and tagging recovery are available.
- Physical disc capture and moving validation failures into Damaged are not exposed.
  Existing Damaged batches can be restored using `restore-quarantined-files`.
- There is no general dry-run/processing plan. `tag-preview` previews metadata;
  `validate` checks files without modifying them.
- There is no shell completion, operation-specific help page, persistent CLI profile
  or general output-directory override. Discovery, shared help and options files
  are available; the installer does not add the CLI to PATH.
- Online lookup, playback and enabled update checks are deferred application
  features, not working desktop operations awaiting a CLI switch.

Windows execution is tested. Linux packages can be cross-built but Linux runtime
testing is outstanding; macOS packaging is still deferred. These platform checks
are separate from command coverage.

## Architecture and tests

Core's `OperationId` and `OperationCatalog` declare identities and capabilities.
`CliOperationCatalog` explicitly exposes or excludes every enum entry; a new entry
without a decision fails compilation. Command names, parsing, formatting and exit
codes stay in CLI. General requests use the enum; conversion engines receive a
separate `ConversionRequest`. See [operation contracts](operations.md).
`OperationExecutionService` calls existing engines and can be reused directly by
a future MCP adapter, without launching CLI subprocesses. The desktop routes remain
unchanged.

`KaraokeToolkit.Tests/Cli` tests argument/settings mapping, safe defaults, JSON,
exit codes, report protection, real extraction/tagging/recovery and the executable
entry point. Core's existing suites retain detailed conversion coverage.
