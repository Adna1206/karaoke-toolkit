# Normalize Audio Gain

Select audio files, karaoke ZIPs or folders in **Audio Tools → Normalize Audio Gain** and
choose an existing output folder outside the selected source folders. The preview
shows each file's measured level, estimated output level and gain change. Changing
the settings reuses the measurements. **Refresh** reads the files again.

| Mode | What it does |
| --- | --- |
| Match loudness | Targets integrated loudness in LUFS. The default is −16 LUFS with a −1 dBTP true-peak ceiling. Tracks that would exceed the ceiling receive less gain and are marked in the preview. |
| Normalize true peak | Targets the highest true peak in each track, measured in dBTP. |
| Adjust by dB | Applies the same signed gain adjustment to each track. An item is skipped when the requested adjustment would exceed the ceiling. |

Each adjustment uses a constant gain for the whole track. Output is
checked for decodability, duration, sample rate, channel count and true peak.
An encoder can raise the true peak. The target modes can lower gain and retry
to stay within the ceiling. The completion report gives the applied gain.
If a fixed adjustment encodes above the ceiling, that item fails.
Files with no measurable level for the chosen mode are skipped.

Supported file types are MP3, AAC in M4A or raw `.aac`, ALAC in M4A, PCM WAV,
FLAC, WMA Standard and Ogg Vorbis. Audio stays in its source format.
Adjusting lossy audio re-encodes it and may reduce quality. A zero-gain
copy retains the source bytes. Metadata and cover art are passed through
where supported by the output format.

For a karaoke ZIP, the tool measures the single audio track and replaces its
audio entry in a new ZIP. The archive must contain one supported audio file and
one CDG with the same filename stem. The rebuilt ZIP uses compatible
Deflate compression and preserves the CDG and other entries. Encrypted,
split or multi-track ZIPs are reported in the preview.

Matching CDG files are copied byte for byte as part of the same publication.
Some lossy containers can add encoder padding. Output whose decoded duration
differs from the source by more than 80 ms is rejected.
Files use their original names in a flat output folder. Conflicting names
within the batch are reported. **Fail** protects existing output files by
default. You can also choose **Skip** or explicitly **Replace** existing
copies. Originals stay in their source folders.

The same operation is available through the [CLI](cli.md#normalize-audio-gain)
and [MCP](mcp.md#normalize-audio-gain). See [crash recovery](crash-recovery.md)
for interrupted runs.

## Change Audio Key

Open **Audio Tools → Change Audio Key**, choose an output folder, and enter a
whole-number shift from −12 to +12 semitones. A negative value lowers the key.
At zero, Apply is disabled. The preview shows the planned shift and a tempo of
100%.

The tool accepts the same audio formats and single-track karaoke ZIPs as
Normalize Audio Gain. It writes new copies, checks decoded duration, and
keeps matching CDG files unchanged. Lossy audio is re-encoded.

Use [CLI](cli.md#change-audio-key) or [MCP](mcp.md#change-audio-key) to plan
or run key changes without the desktop.
