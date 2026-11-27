# File workflow features

The home sidebar lists Karaoke Conversion, Archive Tools, Names & Tags,
Audio Tools, Organization, and Validation & Reports. Recipes appears when
saved recipes exist. Category selection lasts for the current session.

- **Names & Tags** includes filename cleanup, pattern renaming, Track ID separators and tagging.
- **Audio Tools** includes gain normalization and key changes.
- **Organization** includes Track ID grouping, alphabetical grouping and flattening.

Each operation has a star button to add or remove it from Favourites. This section
appears at the top only when it has visible operations. Favourites use the same
operation instances and execution flow as their original sections, in the usual
operation order. Choices are saved immediately using stable operation identifiers.
Open [experimental disc ripping](disc-ripping.md) from the **Tools** menu.

## Input

Drop folders or karaoke files onto the main window, select folders with **Add Folders…**,
or select individual files with **Add Files…**. Picker/drop actions accumulate inputs;
**Clear** resets them. Multiple selections are supported. Typing
into the source box replaces the prior selection. Homogeneous ZIP, BIN, KMA or audio/CDG
file selections choose a matching default conversion action; opening input never
starts a conversion automatically.

Selecting an audio file includes its matching CDG source group. Selecting both members
does not convert the pair twice. Other files in that folder are not converted. An
explicit file of the wrong type for the action is rejected rather than causing a
whole-folder conversion. Missing partners are reported as failures.

Folder selections respect recursion. `Damaged` and `.karaoke-job-*` directories are
excluded. Filename cleanup and standalone CUE/TXT creation still require folder input
because they operate on a folder as a group. Automatic CUE/TXT remains a folder-level
listing and follows the existing setting.

Multi-folder selections create recovery checkpoints for every affected folder before
starting conversion. Following an interruption, those pending folder runs can be
recovered individually. Normal cancellation ends the unstarted runs rather than leaving
them as crash-recovery jobs.

## Convert KMA to MP3+G

Choose **Karaoke Conversion → Convert KMA to MP3+G** and select `.kma` files or
folders. Each successful conversion writes a same-named `.mp3` and `.cdg` pair and
keeps the original KMA. The pair uses the selected overwrite policy and shared
conversion recovery. Automatic CUE/TXT generation is skipped for this operation.

The reader handles interleaved WMA and CDG, separator-based WMA or MP3 with CDG,
and an embedded ZIP containing one matching audio/CDG pair with MP3, WMA or PCM
WAV audio. Embedded MP3 is copied without re-encoding. Mono and stereo WMA or WAV
are encoded as stereo 44.1 kHz MP3 at the saved MP3 bitrate. CDG bytes stay intact.
For recognized MP3 and ZIP layouts, the reader can skip up to 64 KiB of zero
padding before the expected payload. Competing audio/CDG separators require a
verified audio boundary and plausible CDG packets before anything is published.

Tracks with no recognized CDG instructions, unknown graphics instructions, or
more than one second of difference between decoded audio and CDG duration still
publish when the pair passes its structural checks. The activity and report show
a per-track warning. Audio-only, unknown and malformed layouts are reported per
file. Compatibility was checked with generated fixtures. Real KMA files and
lyric playback remain unverified.

## ZIP compression

ZIP operations use SharpCompress. Settings offers Deflate levels 0-9, defaulting
to 5 (Balanced). Level 0 stores files without compression; levels 1-9 trade speed
for smaller files. Stored numeric preferences retain their values when loaded.

BZip2 and LZMA are also available for ZIP creation. The 0-9 setting applies only
to Deflate; BZip2 and LZMA use library defaults. Deflate is recommended for older
karaoke players. Algorithm choices persist in preferences, recipes and recovery.

## Convert Archive to ZIP

Choose **Archive Tools → Convert Archive to ZIP** for non-ZIP files or folders.
Supported extensions are 7z, rar, tar, gz/tgz, bz2/tbz/tbz2, xz/txz, lz and zst;
decoding depends on the archive format and variant. ZIP input is excluded.

The operation preserves member contents, relative paths, empty files/directories
and available modification times. It accepts general archives, not only karaoke
pairs, and does not recursively unpack nested archives. ZIP metadata cannot
represent every source format's permissions, attributes or comments.

Output is beside the source (`album.tar.gz` becomes `album.zip`) using the chosen
ZIP algorithm and Deflate level. Existing-output and Delete Originals settings
apply. No automatic CUE/TXT is generated. General archive contents are checked
during conversion, rather than using the optional karaoke-pair validation precheck.
Payload hashes are verified before publication and requested source deletion.

Encrypted, multi-volume, linked, unsafe and duplicate-path entries are rejected;
members must be smaller than 4 GiB. Failed items can be retried. Cancellation keeps
the current source. Tests include TAR, TGZ, GZip, solid/encrypted 7z, output
algorithms, source retention, overwrite, cancellation, retry and checkpoint resume.

## Conversion progress

The footer animates beside **Scanning for source files…** while a fresh
conversion builds its source list. You can cancel during the scan. If storage
is slow, the scan can stop once the current directory read returns. The activity
view shows completed items out of the batch total once processing starts.

## Review existing conversion outputs

With **Skip existing outputs**, a conversion skips the whole track when a
known output is already present. Output names determined during conversion are
checked before publication.

Set **Existing output files (including CUE/TXT)** to **Ask each time** in Settings.
Standalone desktop conversions publish outputs without conflicts as they finish.
They keep new outputs with conflicts in temporary staging. At the end, one review
lists the conflicting groups across the selected folders, including existing
automatic CUE or TXT outputs where applicable. Select individual groups to
replace, or use **Skip all** and **Replace all** to set the choices together.
Select **Apply choices** to publish the staged replacements and discard the
staged outputs for unchecked groups. Unchecked groups keep their existing files.

Matching outputs for one track are kept together. **Cancel operation**
discards unpublished work and keeps completed changes. If a source or an
observed destination changes while the review is open, publication of that
group fails and the affected files are kept in place.

## Repack ZIP for Compatibility

Choose **Archive Tools → Repack ZIP for Compatibility** for selected ZIP files or
folders containing ZIPs. Folder input follows the recursion setting. The operation
rebuilds the archive using the ZIP algorithm and compression level saved in Settings.
The defaults are **Deflate, level 5**. Deflate is recommended for legacy karaoke
players that cannot read other ZIP compression methods. Choosing BZip2 or LZMA still
uses that setting, but may not improve compatibility with those players.

All member contents and directory structure are preserved, including extra files,
empty files and empty directories. No audio is re-encoded. With **Delete Originals**
enabled, the verified result replaces the source ZIP under its original name, without
an overwrite prompt for that intentional replacement. With deletion disabled, the
original is kept and the result is saved beside it as `name (repacked).zip`; existing
output follows the normal Ask/Skip/Replace/Fail policy. Automatic CUE/TXT
generation does not apply. Selecting ZIP files keeps this operation selected.

Source payloads are checked against ZIP CRCs, then the new archive is checked against
the original payloads before publication. Unsafe or duplicate paths, encrypted or
split archives, corrupt members and members of 4 GiB or larger are rejected. Repacking
does not repair corrupt audio/CDG content or guarantee compatibility with every player.
Cancellation, failed-item retry and crash recovery use the standard conversion workflow.

## Group files and folders

Choose **Organization → Group by Alphabet** and select a mode in the
preview. **Files** groups selected files and files inside selected folders and
their subfolders into A–Z and `#` folders. **Folders** moves the immediate
subfolders of each selected folder into those groups with their contents.

In **Files** mode, **Ignore leading ‘The’** groups `The Example.mp3` under E.
**Skip leading Track ID** starts sorting after a recognized leading ID and the
selected separator. For example, `ABC-0000 - Freddie Hart - Song.zip` goes to F
with ` - ` selected. Choose ` - `, `. `, ` – `, `_`, or **Whitespace**, or type
a custom separator. Spaces in custom punctuation separators count. **Whitespace**
matches one or more spaces or tabs. Filenames without a complete ID followed by
the separator are grouped using the full name. **Ignore leading ‘The’** also works
on the text following the ID. The move keeps the original filename.

Changing a grouping option updates the preview after a short pause.
You can keep editing while the previous results are visible. **Move items**
becomes available when the latest preview is ready. Use **Refresh** to rescan
the inputs. Review the destination and issue tables before choosing **Move items**.
Drag the divider above the skipped items to resize or hide that pane.
The run uses the approved list. A source that disappeared or a destination
that became occupied keeps the affected group in place, including a matching
audio/CDG pair. See [Group by Track ID](track-grouping.md) for numeric
range and trailing-digit grouping.

## Rename by Filename Pattern

Select karaoke files or folders and choose **Names & Tags → Rename by
Filename Pattern**. Pick a **Source pattern** that describes the current names
and a **Destination pattern** that describes the new names. Both fields accept
built-in patterns and custom text. The preview updates after you pause editing
the settings on the left, including **Include subfolders**. Use **Refresh** to
rescan the inputs. Hover over a name in the preview table to see its full path.
Drag the divider above **Skipped items and conflicts** down to shrink or hide it,
and back up to see it again. Select **Rename Files** when the preview is ready.

For example, to rename `ABC 20-02 Song Title - Song Artist.mp3`:

1. Set the source to `%DISC%-%TRACK% %TITLE% - %ARTIST%`.
2. Set the destination to `%DISC%-%TRACK% - %ARTIST% - %TITLE%`.
3. The default options produce `ABC20-02 - Song Artist - Song Title.mp3`.
   Turn off **Remove spaces from Disc ID** to keep `ABC 20`. Turn off
   **Uppercase Disc ID** to retain the source casing, such as `Abc`.

The track keeps its original digits, and the destination must use fields that
the source provides. Matching audio and CDG files are renamed together, even
when only one of the pair is selected. File contents and extensions are
preserved. Unmatched names are shown as skipped. Resolve occupied or duplicate
destinations shown in the preview before applying. The run uses the approved
list and skips a group if its source disappears or a destination becomes
occupied. Interrupted
renames use [file move recovery](crash-recovery.md). Start a new rename in Desktop.

## Flatten One Folder Level

Select one or more folders and choose **Organization → Flatten One Folder Level**. For each
selected folder, the operation moves the immediate contents of its child folders into
the selected folder, then removes child folders that became empty. A deeper folder is
moved intact, so one run removes only one layer. Files already in the selected folder
are untouched. Nested selections covered by a selected parent are processed only once.

An existing file or folder with the same name blocks that item; the source remains in
its child folder, and the child is not removed. `Damaged`, recovery folders and linked
folders are left in place. The operation reports individual moves and skips. Cancelling
keeps moves already completed and leaves the remaining source folders in place.

**Flatten All Folder Levels** visits every descendant folder, moves its files directly
into the selected root, and removes empty folders from the bottom up. Selecting `A/B`
places files from `A/B/C/D` into `A/B`; it never moves them into `A`. Files already in
the selected root remain in place. The same collision and protected-folder rules apply,
so folders containing blocked items are retained. The recursion option in Settings does
not change either flattening operation; choose the operation for the depth you want.

## Experimental disc ripping

Experimental CD+G ripping uses cdrdao. Windows x64 bundles the reader. Linux and
macOS use an installed executable.
See [disc ripping](disc-ripping.md) for capture behaviour, synthetic tests and hardware limits.

## Completion reports

TXT and JSON run reports share headers with the application name/version, date, time
(including UTC offset), and report type. CUE sheets and TXT track listings use the same
fields. Only CUE headers use `REM`, the format's comment marker; plain TXT files do not.
The version comes from the built assembly, and the timestamp remains fixed when a
run report is saved again.


**View Last Run Report…** shows successes, skips, failures, messages and output paths. Filter the
list, select a result and use **Open output folder**, or save a TXT/JSON report.
**Retry failed** uses only the failed groups and the original run's settings and
overwrite policy. Automatic metadata failures retry metadata without needing a source
ZIP/BIN that was already deleted successfully. A retry's report describes that attempt.

Reports are in memory until saved. Starting another operation replaces the prior report;
automatic report history is not included. **View Last Run Report…** is disabled until a
report exists. Restoration is available separately under **Validation & Reports**, including after
reopening the app.

Start is disabled while showing the completed/cancelled results screen. Use **Back**
to choose the next operation; **Retry failed** remains an explicit results action.
During processing, **Back** or **Exit** asks whether to keep working or cancel
and leave. The operation continues while you decide. After confirmation, the
app waits for a safe stopping point. For work without cancellation, it offers
to leave after the operation finishes.

## Remember the last input folder

Enable **Remember last input folder** under **Settings → General → File
processing**. After you pick an input, Add Files and Add Folders suggest the
selected folder or the first selected file's parent. The picker still asks you
to choose inputs. Turning the option off clears the saved folder.

## CUE and TXT output names

Under **Settings → General → File processing → Output naming**, set names for
TXT track listings and CUE sheets. Enter a fixed name or use `{folder}` to insert
the output folder name. The default `{folder}` produces `Party Mix.txt` and
`Party Mix.cue` in a folder named `Party Mix`. The fields preview each filename,
and **Reset output names** restores both defaults. Apply or OK saves the changes.

Standalone generation, automatic conversion listings and disc ripping use these
names wherever they generate CUE/TXT. The app adds `.txt` or `.cue` and validates
each resulting filename. Existing outputs follow the selected overwrite policy.
Recovery uses the names saved with the run.

## Windows Explorer

Windows Setup enables Explorer integration for a new user profile. Existing
preferences and the first Setup decision are kept outside the install directory,
so a later Setup preserves the current registration, including a manual removal.
Portable launches leave registration to the Settings buttons.

Use **Settings → Integrations → Windows Explorer → Enable / update** to register **Open in Karaoke Toolkit**
for folders, folder backgrounds, ZIP/BIN/CDG and supported audio extensions. This is a
per-user classic context-menu command, normally under **Show more options** on Windows
11. It uses no administrator access and does not change default file associations.

**Remove** removes only Toolkit-owned entries. Enable/update again after moving the
application. These buttons apply immediately, independently of Options OK/Cancel.
The Explorer command handles one selected item; use Add Files… or drag-and-drop for a
multi-file selection. A compiled application executable must be present.

## Validation and Damaged

Choose **Validate Karaoke Files** in the operation list and press **Start** to read
files without converting or moving them. Validation is a standalone operation, not
a separate main-panel button. It reports:

| Status | Meaning |
|---|---|
| Valid | Supported structure checks passed. |
| Incomplete | Audio or CDG partner is missing; the existing file may be usable. |
| Ambiguous | Multiple matching audio/CDG candidates exist. |
| Damaged | ZIP integrity, BIN sector size, CDG packet size or empty-media checks failed. |
| Unsupported | The archive layout or feature is unsupported; extra files may be usable. |
| Unreadable | Access failed or files changed during the scan. |

Validation and conversion show an animated footer bar while finding source
files. Once the source count is known, the item and total progress bars show
processing progress.

ZIP checks read entries and verify CRCs without extracting. BIN/CDG checks verify
sector/packet structure. Validation does not decode audio, listen to tracks, render
graphics or certify sync/quality. Non-ZIP archives are reported as unsupported and
can be processed with Convert Archive to ZIP. A structurally valid result has its
limitations in the message. Cancellation returns findings collected so far.

**Settings → General → Validate inputs before conversion** enables an additional
read-only check of each source group immediately before conversion. A group with an
issue is reported as failed and remains unchanged; other groups continue. This setting
defaults to **off**. Conversions already perform mandatory integrity/publication
checks; this preference adds a separate validation pass. Those mandatory checks remain
enabled regardless. The preference persists and is retained for retries and crash recovery.

No result causes an automatic move. In Results, explicitly select issues and choose
**Move selected to Damaged…**, then review the file list and confirm. Both members of
a matching pair move together. Valid/unreadable findings cannot be moved this way.
Files are fingerprinted again before moving; changed files are refused. The
footer shows an animated bar while moving files to Damaged or restoring them.

Each group goes to `source-root/Damaged/<unique-batch>/files/<original-relative-path>`.
The unique batch prevents name collisions. A `manifest.json` is written before the
first move. A failed pair move attempts to restore members already moved; if restoration
is blocked, the manifest and remaining files are retained and the failure is reported.
Existing files are never overwritten.

Use **Validation & Reports → Restore Quarantined Files** and select that manifest to restore originals.
This also handles an interrupted move/restore. It checks hashes and refuses to overwrite
replacement files. Keep the manifest with its batch directory, at the original source
root, until restored. Quarantine manifests are restored explicitly; they do not trigger
the conversion crash-recovery startup prompt. Scan again after restoring.

## Verification

The automated suite covers exact selection scope, multi-folder processing, pair
deduplication, saved retry settings, metadata retries, validation categories and CRCs,
read-only scans, exclusions, move/restore hashes, collision refusal, pair rollback,
interrupted quarantine and report exports. Explorer registration/removal is tested in
an isolated temporary registry branch, without installing live Explorer entries.

For the shared suite and verification commands, see
[test structure](../development/test-structure.md). A named artifact build can avoid replacing
binaries used by a running development instance:

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release -p:ArtifactBuildName=StandaloneTests -p:UsedAvaloniaProducts=
```

The requested pre-processing preview remains in [TODO](../todo.md).
