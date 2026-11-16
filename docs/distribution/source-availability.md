# Source availability register

This register tracks source links for components with source-distribution
obligations or exceptions requiring review. Use it to prepare the download page's
**Third-party source code** section. It complements the
[licence inventory](../../licenses/README.md); it is not a complete dependency list.

Baseline: Toolkit **2026.11.17**, reviewed 2026-11-16. Versions below describe
the pinned distribution, not the latest upstream release.

## Components and source links

| Component / shipped target | Version | Licence | Source link | Evidence / remaining work |
| --- | --- | --- | --- | --- |
| TagLibSharp / all | NuGet 2.3.0 | LGPL-2.1-only | [Release source](https://github.com/mono/taglib-sharp/tree/b5ae84f2e84087bf160bb0471420200dd2b5d809) · [ZIP](https://github.com/mono/taglib-sharp/archive/b5ae84f2e84087bf160bb0471420200dd2b5d809.zip) | Matches upstream 2.3.0.0 release. NuGet metadata has no source commit; exact binary build provenance is unconfirmed. |
| FFmpeg / Windows and Linux x64, Apple Silicon | 9.0.2, Conda-forge | LGPL-3.0-or-later | [Core source archive](https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.gz) · [Runtime locks](../../vendor/versions.md#ffmpeg) | The core archive matches the SHA-256 in the embedded FFmpeg recipes. The source evidence includes the embedded FFmpeg recipes and patches. The Windows dependency archives, matching recipes and librsvg Rust sources are included. Linux and Apple Silicon dependency source assembly is pending. |
| CDRDAO / Windows x64 | 1.2.6 | GPLv2 | [Release source archive](https://github.com/cdrdao/cdrdao/releases/download/rel_1_2_6/cdrdao-1.2.6.tar.gz) | Source archive pinned by checksum; currently also copied beside the executable. |
| MSYS2 runtime / CDRDAO DLL | 3.6.5-1 | LGPL-3.0-or-later with linking exception; additional notices | [Source package](https://repo.msys2.org/msys/sources/msys2-runtime-3.6.5-1.src.tar.zst) | DLL matched byte-for-byte to upstream binary package; source includes patches and recipe. |
| GCC runtime / two CDRDAO DLLs | gcc-libs 15.2.0-1 | GPL-3.0-or-later with GCC Runtime Library Exception 3.1 | [Source package](https://repo.msys2.org/msys/sources/gcc-15.2.0-1.src.tar.zst) | DLLs matched to upstream package. Retain exception terms; this row does not imply every use of GCC imposes source disclosure. |
| GNU libiconv / CDRDAO DLL | 1.18-1 | LGPL-2.1-or-later | [Source package](https://repo.msys2.org/msys/sources/libiconv-1.18-1.src.tar.zst) | DLL matched to upstream package; source includes patches and recipe. |

### FFmpeg dependencies

The [runtime locks](../../vendor/versions.md#ffmpeg) list the exact shipped
package builds. Use their embedded recipes as the starting point for collecting
source archives and patches. The [source manifest](../../vendor/distribution-sources.json)
pins the verified FFmpeg core archive. The
[distribution licensing record](distribution-licensing.md#remaining-ffmpeg-work)
tracks the remaining per-target work.

## Download-page use

- Show the component, version, licence and version-specific source link beside
  the relevant Toolkit release download. Include only that platform's components.
- Keep historical release mappings when dependencies change; avoid linking to a
  moving default branch as if it were the source used for a released binary.
- Preserve the evidence/status distinction. A source link is not proof that all
  corresponding source, patches and build inputs have been identified.
- The chosen approach is a separate source archive attached to the GitHub release,
  linked from its release notes, without adding source to the installer. Prepare
  it with `scripts/Prepare-DistributionSources.ps1 -Archive`. Upstream links above
  record provenance; the release asset will provide the retained source copy.
  The archive includes the reviewed Windows dependency sources. The manifest
  records the remaining provenance and platform gaps.

Toolkit declares GPL-3.0-or-later. Link the matching Toolkit source tag alongside
the installer and the third-party archive, and verify that installer recipients
can access it. A separate Toolkit source ZIP is optional when the tag provides
that access. See [publishing the test build](publishing.md#sharing-a-windows-test-build).

## Maintenance

When changing a dependency or preparing a release, update its version, target,
source reference and status here alongside the licence inventory and vendor
manifests. Keep checksums in the manifests rather than duplicating them here.
Resolve missing provenance explicitly; do not infer a verified match solely from
a similar version number. See [distribution licensing](distribution-licensing.md)
for source preparation and [publishing](publishing.md) for release packaging.
