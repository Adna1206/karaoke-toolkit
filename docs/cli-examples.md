# CLI examples

These examples use PowerShell and a published Windows executable in the current
directory. Replace `.\karaoke-toolkit.exe` with `./karaoke-toolkit` on Linux and
use Linux paths. From source, replace that prefix with
`dotnet run --project KaraokeToolkit.Cli --` (requires .NET 10).
The first `--` belongs to `dotnet run`; a later `--` ends the CLI's own options.
See [CLI reference](cli.md) for defaults, settings and exit codes.

## Discover and check files

```powershell
.\karaoke-toolkit.exe --help
.\karaoke-toolkit.exe operations
.\karaoke-toolkit.exe operations --json
.\karaoke-toolkit.exe validate "D:\Karaoke" --json --quiet
```

Validation is read-only. It checks supported karaoke files for structure, pairing
and ZIP integrity; it does not move damaged files automatically. To save a report:

```powershell
.\karaoke-toolkit.exe validate "D:\Karaoke" --report "D:\Reports\validation.json" --quiet
```

Create `D:\Reports` first and choose a new report filename for every run.
Report files are never overwritten.

## Extract and create karaoke ZIPs

```powershell
.\karaoke-toolkit.exe run zip-to-audiog "D:\Incoming" --no-cue-text
.\karaoke-toolkit.exe run audiog-to-zip --input "D:\Disc One" --input "D:\Disc Two" --zip-algorithm Deflate --zip-level 5
```

Extraction preserves the archived audio format. Both commands keep originals by
default and fail for any group whose output already exists. Add `--overwrite skip`
to leave existing outputs alone, or `--overwrite replace` to replace them.
Add `--no-recurse` to limit ordinary conversion scanning to the selected folder.

## Repack for compatibility

```powershell
.\karaoke-toolkit.exe run repack-zip "D:\Incoming" --zip-algorithm Deflate --zip-level 5 --no-cue-text
```

This keeps the source ZIP and creates its repacked counterpart. To replace source
ZIPs with successfully verified repacks, explicitly choose deletion:

```powershell
.\karaoke-toolkit.exe run repack-zip "D:\Incoming" --zip-algorithm Deflate --zip-level 5 --delete-originals --no-cue-text
```

Deflate is the compatibility default for older karaoke software. Other ZIP-writing
operations use the same algorithm/level preferences.

## Encode BIN audio or lossless Audio+G

```powershell
.\karaoke-toolkit.exe run bin-to-audiog "D:\Karaoke\track.bin" --encoder FLAC --no-cue-text
.\karaoke-toolkit.exe run losslessg-to-mp3g "D:\Lossless Karaoke" --bitrate 192 --validate-inputs --no-cue-text
```

The first extracts lossless audio and graphics from BIN. The second makes MP3+G
from supported lossless audio/CDG pairs, keeping the source files. Its output is
MP3 regardless of the general encoder setting. `--validate-inputs` adds source
checks; normal output checks still apply without that flag.

## Preview tags, then apply them

```powershell
.\karaoke-toolkit.exe tag-preview "D:\Karaoke" --pattern "%DISC%-%TRACK% - %ARTIST% - %TITLE%" --json --quiet
.\karaoke-toolkit.exe tag "D:\Karaoke" --pattern "%DISC%-%TRACK% - %ARTIST% - %TITLE%" --fill-empty-only --preserve-comments --preserve-other-tags
```

The pattern shown is the default. A filename such as
`ABC123-04 - Artist - Song.mp3` yields album `ABC123`, track `4`, artist `Artist`
and title `Song`. Track zero is allowed. Literal separators and token order can
change, for example `--pattern "%DISC%. %ARTIST% - %TITLE%"`.

Preview does not modify media. Applying tags writes metadata in place, including
supported audio inside karaoke ZIPs. The second command fills empty fields and
preserves existing comments and other descriptive tags. Without these flags,
parsed fields replace their existing values and descriptive cleanup is enabled.
BIN and orphan CDG files have no writable tag fields and are skipped.

## Make karaoke video

```powershell
.\karaoke-toolkit.exe run karaoke-to-video "D:\Karaoke\song.zip" --video-container MP4 --video-resolution FullHD --video-smoothing None --no-cue-text
.\karaoke-toolkit.exe run karaoke-to-video "D:\Karaoke\track.bin" --video-container MKV --video-audio-bitrate 192 --video-audio-fallback-only --no-cue-text
```

The operation accepts supported Audio+G, ZIP and BIN inputs. MP4 is the default.
Audio bitrate normally follows the source when detectable; the selected fallback
also applies when metadata is unavailable. The second example always uses
192 kbps audio. No smoothing preserves the original CDG pixel edges.

## Organize or flatten folders

These commands move files or directories. Keeping originals is a conversion
preference, not a way to turn file organization into copying.

```powershell
.\karaoke-toolkit.exe run organize-files "D:\Karaoke" --ignore-leading-the
.\karaoke-toolkit.exe run organize-folders "D:\Artists"
.\karaoke-toolkit.exe run flatten-one-layer "D:\Incoming"
.\karaoke-toolkit.exe run flatten-all-layers "D:\Incoming"
```

Organize Files recursively groups files into A-Z/# buckets. The optional leading-The
rule puts names starting with `The Beatles` into B. Organize Folders groups only
immediate child folders. Flatten One Layer moves the contents of immediate children
into the selected root; Flatten All Layers brings all descendant files into that
root. Empty folders are removed. These scopes do not change with `--no-recurse`.
Choose one of these operations for the layout you want; the examples are independent.

## Reuse conversion settings

Save this as `D:\Settings\karaoke.json`:

```json
{
  "AudioEncoder": "MP3",
  "AudioBitrate": 192,
  "ZipAlgorithm": "Deflate",
  "ZipCompression": 5,
  "RecurseFolders": true,
  "AutoCreateCueText": false,
  "DeleteOldFiles": false
}
```

```powershell
.\karaoke-toolkit.exe run bin-to-zip "D:\Incoming" --options "D:\Settings\karaoke.json" --bitrate 256
```

Explicit flags override JSON, so this run uses 256 kbps. The file contains
conversion settings only; use flags for overwrite policy, tagging and other
request-level choices. The CLI neither reads nor changes saved GUI preferences.

## Recover interrupted work or restore moved files

```powershell
.\karaoke-toolkit.exe recovery-list "D:\Incoming" --json
.\karaoke-toolkit.exe recover "D:\Incoming\.karaoke-job-run-ID\run.json" --quiet
.\karaoke-toolkit.exe run restore-quarantined-files "D:\Karaoke\Damaged\BATCH\manifest.json"
```

Use the exact journal path returned by `recovery-list`, not the placeholder `ID`.
Recovery uses its saved settings; processing flags cannot override it. Recipe
recovery uses its saved snapshot and remaining steps through the same command. Restoration instead uses an actual Damaged
batch manifest to return previously quarantined files to their original locations.
It is a separate operation, not crash recovery.

## Use results in a script

```powershell
$text = & .\karaoke-toolkit.exe validate "D:\Karaoke" --json --quiet
$code = $LASTEXITCODE
$result = $text | ConvertFrom-Json
if ($code -eq 0) { "Validation passed" }
elseif ($code -eq 3) { "No matching files" }
else { $result | ConvertTo-Json -Depth 20 }
```

Capture the exit code immediately. Exit 1 means processing/validation issues,
2 means invalid usage/settings and 130 means cancellation. Progress uses stderr;
`--quiet` suppresses it while keeping the final stdout result.

## Run or recover a recipe

Export a recipe JSON file from the desktop builder. Each step must use Fail, Skip
or Replace instead of Ask. The CLI preserves each step's settings, rather than
replacing them with flags from the invoking shell.

```powershell
.\karaoke-toolkit.exe recipe list "D:\Recipes"
.\karaoke-toolkit.exe recipe show "D:\Recipes\convert-and-tag.json"
.\karaoke-toolkit.exe recipe validate "D:\Recipes\convert-and-tag.json"
.\karaoke-toolkit.exe recipe run "D:\Recipes\convert-and-tag.json" "D:\Incoming" --dry-run --json
.\karaoke-toolkit.exe recipe run "D:\Recipes\convert-and-tag.json" "D:\Incoming" --quiet
.\karaoke-toolkit.exe recovery-list "D:\Incoming" --json
.\karaoke-toolkit.exe recover "D:\Incoming\.karaoke-job-workflow-ID\workflow.json" --quiet
```

The dry run plans the first step and lists later steps as deferred until actual
outputs exist. Recovery uses the journal's recipe snapshot even if the exported
recipe has since changed or been deleted. Use the exact journal path from discovery.

## Preview changes and choose an output folder

```powershell
.\karaoke-toolkit.exe run flatten-all-layers "D:\Incoming" --dry-run --json
.\karaoke-toolkit.exe run zip-to-audiog "D:\Incoming" --output "D:\Converted" --no-cue-text --dry-run
.\karaoke-toolkit.exe run zip-to-audiog "D:\Incoming" --output "D:\Converted" --no-cue-text
.\karaoke-toolkit.exe run create-cue-and-text-files "D:\Converted"
```

Create `D:\Converted` first. Outputs from all selected source folders share that
flat destination; duplicate targets block processing. This mode keeps originals
and rejects `--delete-originals`. For multiple source folders, disable automatic
CUE/TXT generation and create one listing afterward as shown. The destination
must be outside selected input folders. Plans are snapshots, not guarantees of
successful decoding/encoding or protection against later changes to files.

## Move eligible validation failures into Damaged

```powershell
.\karaoke-toolkit.exe quarantine "D:\Karaoke" --dry-run --json
.\karaoke-toolkit.exe quarantine "D:\Karaoke" --json --quiet
```

Only eligible failed groups move; valid files stay in place. The report includes
restoration manifests. Unreadable or otherwise nonmovable findings remain issues.
The ordinary `validate` command continues to be read-only.

## Discover and rip a disc

```powershell
.\karaoke-toolkit.exe disc-list --json
.\karaoke-toolkit.exe disc-rip --device "0,1,0" --output "D:\Ripped" --disc-name "DISC123" --tracks "1,3-5" --format Bin --experimental --dry-run
.\karaoke-toolkit.exe disc-rip --device "0,1,0" --output "D:\Ripped" --disc-name "DISC123" --tracks "1,3-5" --format Zip --encoder MP3 --bitrate 192 --experimental
```

Use the device address returned by `disc-list`, not the sample address. Add
`--cdrdao "C:\Tools\cdrdao.exe"` (or a Unix executable path) to select a manual
installation. Windows CLI builds bundle cdrdao; Linux/macOS can use an installed
copy. Hardware compatibility remains experimental and the CLI never elevates.
A dry run scans the disc but does not read its full image or publish tracks.

## Enable shell help and completion

```powershell
.\karaoke-toolkit.exe help karaoke-to-video
.\karaoke-toolkit.exe run repack-zip --help
(& .\karaoke-toolkit.exe completion powershell | Out-String) | Invoke-Expression
(& .\karaoke-toolkit.exe path powershell | Out-String) | Invoke-Expression
```

The last two commands opt into setup for the current PowerShell session. Review
the printed script first if desired; save it in your profile for future sessions.
The executable itself never edits that profile. Setup paths point to the current
installation, so update them after moving it or switching a versioned payload.

For Bash (replace `bash` with `zsh` for Zsh):

```bash
eval "$(./karaoke-toolkit completion bash)"
eval "$(./karaoke-toolkit path bash)"
karaoke-toolkit --help
```

Completion suggests command and option words, leaving file completion to the shell.
It does not yet select enum values based on the preceding option.
