# Source availability register

This register tracks source links for components with source-distribution
obligations or exceptions requiring review. Use it to prepare the download page's
**Third-party source code** section. It complements the
[licence inventory](../../licenses/README.md); it is not a complete dependency list.

Baseline: Toolkit **2026.11.17**, reviewed 2026-11-15. Versions below describe
the pinned distribution, not the latest upstream release.

## Components and source links

| Component / shipped target | Version | Licence | Source link | Evidence / remaining work |
| --- | --- | --- | --- | --- |
| TagLibSharp / all | NuGet 2.3.0 | LGPL-2.1-only | [Release source](https://github.com/mono/taglib-sharp/tree/b5ae84f2e84087bf160bb0471420200dd2b5d809) · [ZIP](https://github.com/mono/taglib-sharp/archive/b5ae84f2e84087bf160bb0471420200dd2b5d809.zip) | Matches upstream 2.3.0.0 release. NuGet metadata has no source commit; exact binary build provenance is unconfirmed. |
| FFmpeg / Windows and Linux x64 | n8.1.3-27-g8dca1586f4 | LGPL-3.0-or-later | [Core source](https://github.com/FFmpeg/FFmpeg/tree/8dca1586f468bfcbb78ae3ed618af1361d9f140a) · [Build recipe](https://github.com/FFmpeg-Builds/FFmpeg-Builds/tree/5031c6f1082414e53688d39e521849905071070d) | Core and recipe pinned; complete external-library sources and notices remain unresolved. Core source alone is insufficient. |
| FFmpeg / macOS ARM64 | 9.0.2, Martin Riedl build | GPL-3.0-or-later | [Core source archive](https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz) · [Vendor build metadata](https://ffmpeg.martin-riedl.de/download/macos/arm64/1789931890_9.0.2/versions.txt) | Exact recipe and complete dependency inputs, including x264 revision, remain unresolved. |
| CDRDAO / Windows x64 | 1.2.6 | GPLv2 | [Release source archive](https://github.com/cdrdao/cdrdao/releases/download/rel_1_2_6/cdrdao-1.2.6.tar.gz) | Source archive pinned by checksum; currently also copied beside the executable. |
| MSYS2 runtime / CDRDAO DLL | 3.6.5-1 | LGPL-3.0-or-later with linking exception; additional notices | [Source package](https://repo.msys2.org/msys/sources/msys2-runtime-3.6.5-1.src.tar.zst) | DLL matched byte-for-byte to upstream binary package; source includes patches and recipe. |
| GCC runtime / two CDRDAO DLLs | gcc-libs 15.2.0-1 | GPL-3.0-or-later with GCC Runtime Library Exception 3.1 | [Source package](https://repo.msys2.org/msys/sources/gcc-15.2.0-1.src.tar.zst) | DLLs matched to upstream package. Retain exception terms; this row does not imply every use of GCC imposes source disclosure. |
| GNU libiconv / CDRDAO DLL | 1.18-1 | LGPL-2.1-or-later | [Source package](https://repo.msys2.org/msys/sources/libiconv-1.18-1.src.tar.zst) | DLL matched to upstream package; source includes patches and recipe. |

### Libraries inside FFmpeg

These are part of the FFmpeg source review, not separate NuGet dependencies.
The [source manifest](../../vendor/distribution-sources.json) records available
archives and hashes; [distribution licensing](distribution-licensing.md#remaining-ffmpeg-work)
records the detailed gaps. Do not present candidate sources as verified matches.

| Component | Recorded version / revision | Source reference | Status |
| --- | --- | --- | --- |
| LAME | Windows/Linux recipe SVN r7113; macOS reported 4.0 | [SVN r7113](https://svn.code.sf.net/p/lame/svn/!svn/rvr/7113/trunk/lame/) | LGPL; exact inputs and complete source assembly need review per target. |
| libbluray | Windows DLL identifies 1.5.1; BtbN recipe pins `f33a738f` | [Pinned source](https://code.videolan.org/videolan/libbluray/-/tree/f33a738fd7bb8b5e1148b0a5424778db7342450b) | LGPL; its recipe builds a static library. |
| libudfread | BtbN recipe pins `b0bc6957` | [Pinned source](https://code.videolan.org/videolan/libudfread/-/tree/b0bc6957e7e07d5f35391f210596d9eb71cffdd9) | Recipe supplies this as libbluray's external dependency. The other archived revision is the fallback submodule. |
| TwoLAME | BtbN recipe pins `6fced852` | [Pinned source](https://github.com/njh/twolame/tree/6fced852d4d5cfad58cf9dbe3ea619b08e87d398) | Windows binary reports `--enable-libtwolame`; recipe builds it statically. |
| GNU libiconv | BtbN recipe pins `b5a1735e`, with gnulib `5df606bb` | [Pinned source archive](https://git.savannah.gnu.org/gitweb/?p=libiconv.git;a=snapshot;h=b5a1735e02bf43ee6e490fcf9395e8abc8d32432;sf=tgz) | Windows binary reports `--enable-iconv`. Linux linkage is a separate question. |
| x264 | macOS vendor reports 0.165.x | [Upstream repository](https://code.videolan.org/videolan/x264) | GPL; moving recipe input, exact revision unresolved. |

This subsection is not exhaustive: enabled and transitive components still need
review per target. Permissive dependencies still need their required notices.

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
  The current archive remains incomplete until the source gaps are closed.
  No download page or source archive has been published yet.

Toolkit currently declares GPL-3.0-or-later. Its owner intends to decide when to
publish the application's source; confirm the intended application distribution
terms separately. Do not treat this third-party source archive as Toolkit source
or change the application's licence without an explicit decision.

## Maintenance

When changing a dependency or preparing a release, update its version, target,
source reference and status here alongside the licence inventory and vendor
manifests. Keep checksums in the manifests rather than duplicating them here.
Resolve missing provenance explicitly; do not infer a verified match solely from
a similar version number. See [distribution licensing](distribution-licensing.md)
for source preparation and [publishing](publishing.md) for release packaging.
