# Vendored software versions

This inventory records binaries stored under `vendor/`. Update it and the relevant checksum manifest when a binary changes. NuGet packages are versioned in the project files.

## FFmpeg

| Target | Version | Source build | License | Archive SHA256 | Status |
|---|---|---|---|---|---|
| `win-x64` | `n8.1.3-6-gff48edd8b2` | [BtbN autobuild 2026-09-29](https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-09-29-13-10) | LGPL v3, per included `LICENSE.txt` | `1c9af2356443fec537fe1a64a5b33cb4c54fa212ad6590464423b3437e1aaa44` | Stored and tested |
| `osx-x64` | `9.0.2` | [Evermeet](https://evermeet.cx/ffmpeg/) | GPL v3 or later, per binary build | `ffmpeg`: `4acc0be580f9b2788029eb7bd4d645ff87968911b0a62aeeb3940d42d54558d5`; `ffprobe`: `24a9c968cd4da72d99c7245e914b921815835eb6dff01d99868031aebaf1d439` | Cross-publish checked on Windows; Mac runtime untested |
| `osx-arm64` | `9.0` (site label) | [OSXExperts](https://www.osxexperts.net/) | GPL v2 or later, per binary build | `ffmpeg`: `d0c06c5c68ce48af3143b262f7a9118a7c9f67de1e237fcc24ffb14df9c67af9`; `ffprobe`: `0c94fbdd8917022f28115eca512196cf4648732bc9e5db9ec8896c7e519d02aa` | Cross-publish checked on Windows; Mac runtime untested |
| `linux-x64` | `n8.1.3-6-gff48edd8b2` | [BtbN autobuild 2026-09-29](https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-09-29-13-10) | LGPL v3, per included `LICENSE.txt` | `10058038d7e05869a32bb7d8594dd1a0b4032b8a10f1387dbe7e5ce4f978b8c0` | Stored; cross-publish checked on Windows |

The complete Windows and Linux archives are in `ffmpeg/<rid>/`; the macOS vendors publish FFmpeg and FFprobe as separate ZIPs. `ffmpeg/manifest.json` pins each archive name, checksum and provenance. Build preparation reads only local archives. Both macOS builds enable GPL code, so they require separate redistribution review and corresponding source before a public release. OSXExperts labels its downloads "for educational purposes only"; the page does not establish complete redistribution provenance. The paired archives are retained for local development and require testing on each Mac architecture.

## CDRDAO

| Target | Version | Release | License | Archive SHA256 | Status |
|---|---|---|---|---|---|
| Source | 1.2.6 | [Official release](https://github.com/cdrdao/cdrdao/releases/tag/rel_1_2_6) | GPL v2 (`COPYING` in source) | `10d15b0dc78bd44a9a4a8d7d85498c734771036159d05aa940e17f18fc40d40f` | Stored |
| `win-x64` | 1.2.6 | [Official release](https://github.com/cdrdao/cdrdao/releases/tag/rel_1_2_6) | GPL v2 for CDRDAO; runtime notices under `cdrdao/notices/` | `653e56c49f94771f87cd9f4ceefc255329714ba5066bd78e30edaa464c322a3a` | Bundled experimental reader; version command checked; hardware untested |
| `linux-x64`, `osx-x64`, `osx-arm64` | — | Official release has no binaries | — | — | Target builds needed |

`cdrdao/manifest.json` records these assets. The four Windows runtime DLLs are
matched to archived MSYS2 packages in `cdrdao/runtime-components.json`; their
notices are shipped and matching source packages are pinned in
`distribution-sources.json`. See [distribution licensing](../docs/distribution-licensing.md)
for source assembly and unresolved FFmpeg inputs. The experimental disc reader
uses cdrdao. Linux/macOS require an installed executable; no bundled builds are present.
