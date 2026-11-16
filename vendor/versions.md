# Pinned software versions

Native archives are fetched on demand using the exact URLs and hashes in the
platform manifests. See [runtime preparation](../docs/distribution/audio-runtime.md#runtime-preparation).

This inventory records native binaries pinned under `vendor/`. Update it and the relevant checksum manifest when a binary changes. NuGet packages are versioned in the project files.

## FFmpeg

| Target | Conda-forge FFmpeg 9.0.2 build | Runtime lock | Verification |
|---|---|---|---|
| `win-x64` | `lgpl_h9d1ee69_608` | [Windows x64](ffmpeg/conda-win-x64.json) | Windows test suite and installer packaging |
| `linux-x64` | `lgpl_hff9caec_808` | [Linux x64](ffmpeg/conda-linux-x64.json) | Cross-preparation and ELF dependencies checked |
| `osx-arm64` | `lgpl_ha3d7a35_8` | [Apple Silicon](ffmpeg/conda-osx-arm64.json) | Cross-preparation and Mach-O dependencies checked |

The three FFmpeg builds enable LGPL version 3. [Conda-forge package index](https://anaconda.org/conda-forge/ffmpeg/files?version=9.0.2)

## CDRDAO

| Target | Version | Release | License | Archive SHA256 | Status |
|---|---|---|---|---|---|
| Source | 1.2.6 | [Official release](https://github.com/cdrdao/cdrdao/releases/tag/rel_1_2_6) | GPL v2 (`COPYING` in source) | `10d15b0dc78bd44a9a4a8d7d85498c734771036159d05aa940e17f18fc40d40f` | Stored |
| `win-x64` | 1.2.6 | [Official release](https://github.com/cdrdao/cdrdao/releases/tag/rel_1_2_6) | GPL v2 for CDRDAO; runtime notices under `../licenses/cdrdao/` | `653e56c49f94771f87cd9f4ceefc255329714ba5066bd78e30edaa464c322a3a` | Bundled experimental reader; version command checked; hardware untested |
| `linux-x64`, `osx-arm64` | — | Official release has no binaries | — | — | Target builds needed |

`cdrdao/manifest.json` records these assets. The four Windows runtime DLLs are
matched to archived MSYS2 packages in `cdrdao/runtime-components.json`; their
notices are shipped and matching source packages are pinned in
`distribution-sources.json`. See [distribution licensing](../docs/distribution/distribution-licensing.md)
for source assembly and unresolved FFmpeg inputs. The experimental disc reader
uses cdrdao. Linux/macOS require an installed executable; no bundled builds are present.
