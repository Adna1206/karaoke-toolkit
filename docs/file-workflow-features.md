# File workflow features

The home screen has a category sidebar and an operation list for Karaoke Conversion,
Archive Tools, File & Folder Tools, and Validation & Reports. Disc Ripping appears
when its experimental setting is enabled, and Recipes appears when saved recipes
exist. Selecting an operation keeps its category synchronized. Unavailable online
renaming is hidden. Category selection is session state, not an expander preference.

Each operation has a star button to add or remove it from Favourites. This section
appears at the top only when it has visible operations. Favourites use the same
operation instances and execution flow as their original sections, in the usual
operation order. Choices are saved immediately using stable operation identifiers.
A favourited disc-ripping action is hidden while ripping is disabled, but its star
is remembered.

## Input

Drop folders or karaoke files onto the main window, select folders with **Add Folders…**,
or select individual files with **Add Files…**. Picker/drop actions accumulate inputs;
**Clear** resets them. Multiple selections are supported. Typing
into the source box replaces the prior selection. Homogeneous ZIP, BIN or audio/CDG
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

## Repack ZIP for Compatibility

Choose **Archive Tools → Repack ZIP for Compatibility** for selected ZIP files or
folders containing ZIPs. Folder input follows the recursion setting. The operation
rebuilds the archive using the ZIP algorithm and compression level saved in Options;
the defaults are **Deflate, level 5**. Deflate is recommended for legacy karaoke
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

## Flatten Folder Layer

Select one or more folders and choose **File & Folder Tools → Flatten Folder Layer**. For each
selected folder, the operation moves the immediate contents of its child folders into
the selected folder, then removes child folders that became empty. A deeper folder is
moved intact, so one run removes only one layer. Files already in the selected folder
are untouched. Nested selections covered by a selected parent are processed only once.

An existing file or folder with the same name blocks that item; the source remains in
its child folder, and the child is not removed. `Damaged`, recovery folders and linked
folders are left in place. The operation reports individual moves and skips. Cancelling
keeps moves already completed and leaves the remaining source folders in place.

**Flatten All Folder Layers** visits every descendant folder, moves its files directly
into the selected root, and removes empty folders from the bottom up. Selecting `A/B`
places files from `A/B/C/D` into `A/B`; it never moves them into `A`. Files already in
the selected root remain in place. The same collision and protected-folder rules apply,
so folders containing blocked items are retained. The recursion option in Settings does
not change either flattening operation; choose the operation for the depth you want.

## Experimental disc ripping

Experimental CD+G ripping now uses cdrdao on all supported desktop platforms;
Windows x64 bundles the reader, while Linux/macOS currently use an installed executable.
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

## Windows Explorer

Use **Settings → Explorer → Enable / update** to register **Open in Karaoke Toolkit**
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
Files are fingerprinted again before moving; changed files are refused.

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
[test structure](test-structure.md). A named artifact build can avoid replacing
binaries used by a running development instance:

```powershell
dotnet test KaraokeToolkit.Tests/KaraokeToolkit.Tests.csproj -c Release -p:ArtifactBuildName=StandaloneTests -p:UsedAvaloniaProducts=
```

The requested pre-processing preview remains in [TODO](todo.md).
