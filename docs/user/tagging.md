# Karaoke tagging

Choose **Names & Tags → Tag Karaoke Files**, select media or folders and
click Start. The main workspace previews filename-derived tags, shows current values,
and lets you correct fields or exclude rows before **Apply Tags**. Changing the
pattern recalculates the proposed tags from the last scan after you pause typing.
Use **Refresh** to read the files and their current tags again.
Use **Select all** or **Deselect all**
to update which editable rows are included. Folder recursion uses the existing preference.
The preview animates its progress bar while scanning inputs, then shows how many
files it has checked once the file count is known. **Back** returns to the
selected operation and inputs. Confirm before leaving a preview with edits.

The dropdown offers these built-in patterns.

| Pattern | Example filename stem |
| --- | --- |
| `%DISC%-%TRACK% - %ARTIST% - %TITLE%` (default) | `ABC123-04 - Artist - Title` |
| `%DISC% - %ARTIST% - %TITLE%` | `ABC123-04 - Artist - Title` |
| `%DISC%-%TRACK% - %TITLE% - %ARTIST%` | `ABC123-04 - Title - Artist` |
| `%ARTIST% - %TITLE% - %DISC%-%TRACK%` | `Artist - Title - ABC123-04` |
| `%TITLE% - %ARTIST% - %DISC%-%TRACK%` | `Title - Artist - ABC123-04` |
| `%ARTIST% - %TITLE%` | `Artist - Title` |
| `%TITLE% - %ARTIST%` | `Title - Artist` |

For an identifier such as `ABC-1234567`, the default previews `ABC` in Album
and `1234567` in Track. With `%DISC% - %ARTIST% - %TITLE%`, the preview
proposes `ABC-1234567` for Album and preserves the existing Track value.
Use that whole-ID pattern for a filename such as `ABC-369A - Artist - Title`
to keep `ABC-369A` in Album. The Track tag accepts a number.

You can also choose a previously accepted pattern or type your own. Tokens
can be reordered or omitted, and separators are literal text. For example
`%DISC%. %ARTIST% - %TITLE%` reads `ABC123. Artist - Title`. The last accepted
pattern is filled in when you reopen the preview. Applying tags adds custom
patterns to the dropdown history.

| Token | Metadata field |
| --- | --- |
| `%DISC%` | Album, retaining alphanumeric karaoke disc identifiers |
| `%TRACK%` | Numeric Track; leading zeros become a number, e.g. 04 → 4 |
| `%ARTIST%` | Artist |
| `%TITLE%` | Title |

Tokens are case-insensitive and appear at most once. Adjacent tokens need a
separator. Unknown tokens, missing matches and nonnumeric or negative tracks are
reported as preview issues. With the default pattern,
`ABC-123 - Artist - Title - Live` previews Album `ABC`, Track `123`,
Artist `Artist` and Title `Title - Live`. With `%ARTIST% - %TITLE%`,
`Artist - Title - Live` produces the same artist and title. Review the split
if the artist itself contains ` - `. Fields omitted from the pattern keep
their existing tags. To change filenames, use
[Rename by Filename Pattern](file-workflow-features.md#rename-by-filename-pattern).
Track 0 is accepted, including `00` and `0000`. Containers that represent zero as
an unset track may display it as blank rather than `0` in another media player.
The default pattern reads `ABC1309-5-04 - Artist - Title` as Album
`ABC1309-5`, Track `4`, Artist `Artist` and Title `Title`.

**Fill empty fields only** preserves populated Album/Track/Artist/Title. Otherwise
the fields supplied by the pattern replace matching existing values.
**Clear existing comments** and **Clear other descriptive tags** default on.
The latter clears genre, year, lyrics, composers, conductor, copyright, grouping,
album artists, numeric disc, totals, BPM and sort fields where supported. It does
not blindly strip all tags: artwork, encoder delay and playback metadata remain.
These controls affect this run; the saved history concerns filename patterns.

## Formats and safety

TagLibSharp 2.3.0 is an unmodified managed NuGet dependency for MP3, OGG/Opus,
WAV, FLAC, APE, WMA, AIFF, ALAC/M4A, WavPack, MP4 and AVI. MKV/WebM use bundled
FFmpeg stream copying because the released library cannot save Matroska tags.
BIN/CDG have no standard tag area and are skipped with an explanation. Selecting
a CDG chooses a unique matching audio file; selecting both is deduplicated.
Standalone audio/video files can also be tagged without a CDG companion.

ZIPs must contain exactly one matching audio/CDG pair. Tags come from the ZIP's
external filename, even if internal filenames differ. Member names/paths and CDG
bytes are preserved. The rebuilt archive uses the configured ZIP algorithm/level.
Encrypted, unsafe, damaged and ambiguous ZIPs are rejected.

Writes update files **in place**, regardless of Delete Originals or conversion
overwrite policy. Nothing is re-encoded. Work happens in a transaction workspace
on the same volume; the source fingerprint must still match the preview. Tags are
read back and compressed media packets hashed before/after. ZIP members are
checked against the staged audio and original graphics. Verification failure,
read-only inputs and cancellation before publication keep originals unchanged.
Completed files remain after batch cancellation.

Tagging batches checkpoint the accepted, manually edited preview, tagging options,
ZIP processing settings and completed files. Apply uses the approved list and
checks the saved fingerprint when each file starts. Startup, revisiting a selected folder
and **Recover** offer **Recover and resume** or **Later**. Recovery verifies the
interrupted publication, keeps completed files, and applies the saved tags to
remaining files without reopening the pattern dialog. An unprocessed file with a
changed fingerprint is left in place and reported. Changes to files involved in
interrupted publication can block recovery. Normal cancellation ends the batch and retains
completed work. Old unindexed tagging transaction markers can still be recovered
when revisiting their folder, followed by a fresh preview. Recipe tagging uses saved
pattern/options without a dialog; its generated preview and completed files are
checkpointed inside the parent recipe rather than a separate tagging recovery job.

Results use the standard session report header with **Tagging Report** as type.
The About page lists TagLibSharp and displays its shipped LGPL v2.1 text; see
[licence inventory](../../licenses/README.md) for source/distribution information.
Automated tests cover writable audio/video formats, packet and decoded-audio
preservation, settings history, ZIPs, failure/cancellation and recovery. Native
execution on Linux/macOS and uncommon third-party media variants still need testing.
