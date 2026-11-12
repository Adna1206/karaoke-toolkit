# Experimental cdrdao CD+G reader

The application uses `NativeDiscReader`, a managed adapter that launches cdrdao
as a separate process. No BASS DLL or x86 .NET helper is required by the app.
The adapter uses only read commands: `scanbus`, `read-toc` and `read-cd`.

Windows x64 builds copy the checksum-verified cdrdao 1.2.6 executable and its MSYS
runtime DLLs from `vendor/cdrdao`. The exact cdrdao source archive and GPL notice
are copied alongside it. Linux and macOS currently require a separately installed
cdrdao on PATH, or an absolute executable path in `KARAOKETOOLKIT_CDRDAO`.
The same override can be used on Windows. In Options → CD+G Ripping, an optional
executable picker saves a full cdrdao file path on any platform. This takes priority
over the environment override; clearing it restores environment/bundled/PATH lookup.
Apply or OK saves the preference for the next ripping window. No runtime downloads are performed.
Bundled Linux/macOS builds have not been supplied, and runtime/drive access on
those platforms is not verified. On macOS the drive may need to be unmounted;
Linux users may need device access permissions. The app does not elevate privileges.

## Capture and conversion

Refresh discovers device addresses from both process output streams and reads a
TOC with raw R-W subchannels. A failed inspection remains visible with its diagnostic.
Only audio/CD+G discs are offered; mixed-mode discs and unsupported TOC layouts are
rejected. The saved identity is a fingerprint of track offsets, lengths and pregaps,
not a unique content hash of the physical disc.

One full-disc image is captured when the first selected track is read, then shared
by all selected tracks in that run. This requires temporary space for the whole disc
and can take time even when one track is selected. The captured TOC must match the
Refresh snapshot, and the image must have exactly the expected length.

Track extraction omits its index-0 pregap, swaps big-endian audio samples to
little-endian PCM, removes P/Q bits and deinterleaves raw CDG packet symbols across
three sectors. Lookahead crosses track boundaries. At the end of the disc, the final
two sectors' graphics are blanked because the necessary future symbols are absent;
audio duration is preserved. This does not implement subchannel error correction.
The interleave layout is independently implemented using the standard offset mapping
also described by [cdgtools](https://github.com/NotTheRealJoe/cdgtools/blob/master/cdgparse.py).
cdrdao's format and raw/cooked modes are documented in its
[manual](https://github.com/cdrdao/cdrdao/blob/master/dao/cdrdao.man).

The existing staged BIN/Audio+G/ZIP pipeline verifies and publishes each selected
track. Ripping shares the automatic CUE/TXT preference, configured only in General.
After successful tracks are published, BIN output generates a
CUE sheet and TXT listing; Audio+G and ZIP generate a TXT listing. Reports describe
the matching files in the destination folder and use the usual existing-file policy,
including Ask/Skip/Replace. Metadata failures leave completed tracks intact.
Failed tracks cannot overwrite existing outputs. Cancellation kills the owned
process and discards unpublished staging; completed tracks remain. Temporary images
are removed on normal completion, failure and cancellation. A hard app crash can leave
`karaoke-cdrdao-*` temporary folders; disc reads still require Refresh and a new run.
Capture/scan deadlines are 30 minutes/5 minutes. Progress remains at zero during the
external capture, then advances during track extraction.

## Verification and limits

Hardware-free tests exercise Windows SCSI, Linux device and macOS IORegistry
addresses; TOC parsing; PCM byte order; raw subchannel deinterleaving; track boundaries;
pregaps; disc-tail handling; one capture per batch; changed/truncated images;
cancellation; process errors; and cleanup. They compare BIN, WAV+G and ZIP payloads
against explicit synthetic CD+G sector fixtures. Existing ripping
tests also cover lossy encoding, overwrite prompts and GUI behaviour.

These are format and workflow tests using synthetic images. Real drive support, damaged-disc performance,
audio/graphics timing and visual correctness still require hardware tests on each OS.
No legacy bridge or original native-library bundle is needed to run these tests.
