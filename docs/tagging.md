# Karaoke tagging

Choose **File & Folder Tools → Tag Karaoke Files**, select media or folders and
click Start. The dialog previews filename-derived tags, shows current values,
and lets you correct fields or exclude rows before **Apply Tags**. Changing the
pattern requires a fresh preview. Folder recursion uses the existing preference.

The default is `%DISC%-%TRACK% - %ARTIST% - %TITLE%`. Tokens can be reordered or
omitted, and separators are literal text. For example `%DISC%. %ARTIST% - %TITLE%`
reads `ABC123. Artist - Title`. Accepted patterns are remembered uniquely in the
dropdown across restarts; the last accepted pattern is selected next time.
Cancelled or merely previewed combinations are not added.

| Token | Metadata field |
| --- | --- |
| `%DISC%` | Album, retaining alphanumeric karaoke disc identifiers |
| `%TRACK%` | Numeric Track; leading zeros become a number, e.g. 04 → 4 |
| `%ARTIST%` | Artist |
| `%TITLE%` | Title |

Tokens are case-insensitive and appear at most once. Adjacent tokens need a
separator. Unknown tokens, missing matches and nonnumeric or negative tracks are
reported without guessing. Separators inside artist/title names can be ambiguous:
the preview shows the literal split for review and manual correction. Fields
omitted from a pattern retain their existing values; this is not a filename-renaming tool.
Track 0 is accepted, including `00` and `0000`. Containers that represent zero as
an unset track may display it as blank rather than `0` in another media player.
Hyphenated disc identifiers stay together: the default pattern reads
`KJ1309-5-04 - Lana Del Ray - Summertime Sadness` as Album `KJ1309-5`, Track `4`,
Artist `Lana Del Ray` and Title `Summertime Sadness`. It does not infer a separate
numeric Disc field from part of the identifier.

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
ZIP processing settings and completed files. Startup, revisiting a selected folder
and **Recover** offer **Recover and resume** or **Later**. Recovery verifies the
interrupted publication, keeps completed files, and applies the saved tags to
remaining files without reopening the pattern dialog. Changed files block recovery
rather than overwrite newer work. Normal cancellation ends the batch and retains
completed work. Old unindexed tagging transaction markers can still be recovered
when revisiting their folder, followed by a fresh preview. Recipe tagging uses saved
pattern/options without a dialog; its generated preview and completed files are
checkpointed inside the parent recipe rather than a separate tagging recovery job.

Results use the standard session report header with **Tagging Report** as type.
The About page lists TagLibSharp and displays its shipped LGPL v2.1 text; see
[licence inventory](../licenses/README.md) for source/distribution information.
Automated tests cover writable audio/video formats, packet and decoded-audio
preservation, settings history, ZIPs, failure/cancellation and recovery. Native
execution on Linux/macOS and uncommon third-party media variants still need testing.
