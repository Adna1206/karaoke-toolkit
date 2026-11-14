# Pinned software versions

Native archives are fetched on demand using the exact URLs and hashes in the
platform manifests. See [runtime preparation](../docs/audio-runtime.md#runtime-preparation).

This inventory records binaries stored under `vendor/`. Update it and the relevant checksum manifest when a binary changes. NuGet packages are versioned in the project files.

## FFmpeg

| Target | Version | Source build | License | Archive SHA256 | Status |
|---|---|---|---|---|---|
| `win-x64` | `n8.1.3-27-g8dca1586f4` | [BtbN autobuild 2026-10-31](https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/tag/autobuild-2026-10-31-12-59) | LGPL v3, per included `LICENSE.txt` | `5cef7bc48e513f3cee7c881e8b2999d835541edb1846c3e89443a67f8f8556ab` | Stored and tested |
| `osx-arm64` | `9.0.2` | [Martin Riedl](https://ffmpeg.martin-riedl.de/#download-release-build) | GPL v3 or later, per vendor configuration | `ffmpeg`: `c8ed4c4e6978a03c485edbfe4e0a5dc2380f8a30bba5150531b31b094492d924`; `ffprobe`: `fcbe839537485eaee7a7a8bc5cbc0f90d53617e80943e8a5b2e31cb851197ea6` | Checksums, ARM64 headers and preparation checked on Windows; native Mac runtime untested |
| `linux-x64` | `n8.1.3-27-g8dca1586f4` | [BtbN autobuild 2026-10-31](https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/tag/autobuild-2026-10-31-12-59) | LGPL v3, per included `LICENSE.txt` | `493e89a48da02b9a7d217b60f7f60c3c917c9bed73459d8a106e5e987da082ef` | Stored; extraction and layout checked on Windows |

The complete Windows and Linux archives are in `ffmpeg/<rid>/`; the macOS vendors publish FFmpeg and FFprobe as separate ZIPs. `ffmpeg/manifest.json` pins each archive name, checksum and provenance. Build preparation reads only local archives. The Apple Silicon build enables GPL code and needs complete corresponding source before a public release. The paired archives require native testing on Apple Silicon.

## CDRDAO

| Target | Version | Release | License | Archive SHA256 | Status |
|---|---|---|---|---|---|
| Source | 1.2.6 | [Official release](https://github.com/cdrdao/cdrdao/releases/tag/rel_1_2_6) | GPL v2 (`COPYING` in source) | `10d15b0dc78bd44a9a4a8d7d85498c734771036159d05aa940e17f18fc40d40f` | Stored |
| `win-x64` | 1.2.6 | [Official release](https://github.com/cdrdao/cdrdao/releases/tag/rel_1_2_6) | GPL v2 for CDRDAO; runtime notices under `../licenses/cdrdao/` | `653e56c49f94771f87cd9f4ceefc255329714ba5066bd78e30edaa464c322a3a` | Bundled experimental reader; version command checked; hardware untested |
| `linux-x64`, `osx-arm64` | — | Official release has no binaries | — | — | Target builds needed |

`cdrdao/manifest.json` records these assets. The four Windows runtime DLLs are
matched to archived MSYS2 packages in `cdrdao/runtime-components.json`; their
notices are shipped and matching source packages are pinned in
`distribution-sources.json`. See [distribution licensing](../docs/distribution-licensing.md)
for source assembly and unresolved FFmpeg inputs. The experimental disc reader
uses cdrdao. Linux/macOS require an installed executable; no bundled builds are present.
