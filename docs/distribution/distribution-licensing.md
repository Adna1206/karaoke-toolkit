# Distribution licensing

The [source availability register](source-availability.md) collects versions,
source links and outstanding matches for download-page preparation. This guide
retains the detailed evidence and investigation notes.

Karaoke Toolkit's original code and assets are licensed under **GPL-3.0-or-later**.
Copyright © 2026 André Almeida (ADNA). See [LICENSE](../../LICENSE)
and [the application notice](../../NOTICE.txt). Third-party components retain their
own licences. Application licensing does not resolve the remaining dependency
source-distribution work recorded below.

## Verified source evidence

`vendor/distribution-sources.json` records downloaded source archives by SHA256.
The source evidence can be assembled offline after those archives are cached:

```powershell
.\scripts\Prepare-DistributionSources.ps1
# Prepare the Windows cdrdao and MSYS2 source bundle independently:
.\scripts\Prepare-DistributionSources.ps1 -CdrdaoOnly
# Explicitly permit downloading missing pinned source archives:
.\scripts\Prepare-DistributionSources.ps1 -Download
# Also create a ZIP and SHA-256 sidecar, verifying every archived file:
.\scripts\Prepare-DistributionSources.ps1 -Archive
```

Output is a fresh directory under ignored `artifacts/distribution-sources/`,
containing the archives, checksums, manifest and this document. It is deliberately
labelled incomplete. Ordinary builds do not download these sources or add their
size to installed packages. No public hosting or source offer is configured.
`-Archive` writes a sibling ZIP named with the Toolkit version and `incomplete`,
plus a `.sha256` sidecar. It includes the current full source inventory (including
other platforms), notices and provenance documents. Combine with `-CdrdaoOnly`
for that narrower scope, or `-Download` to fetch missing pinned inputs. ZIP
creation checks entry counts, lengths and SHA-256 for every file before publishing
the local archive. This verifies archive integrity, not source completeness.
When the inventory is complete, arrange source access alongside the binaries
under the applicable licence terms; generating local evidence is not publication.
The cdrdao-only output includes its four source archives, licence texts, DLL
provenance and checksums. It can be reviewed separately from FFmpeg. Link the
matching source bundle directly from the page that offers the binary download.

This also applies to vendor archives downloadable through a public Git repository,
including Git LFS objects referenced by older commits. Review each distinct build
that will remain downloadable when publishing the repository or its history.

### Windows cdrdao

All four runtime DLLs from the pinned cdrdao ZIP were matched byte-for-byte to
archived MSYS2 packages. `vendor/cdrdao/runtime-components.json` records hashes,
binary-package origins and matching source archives:

| Files | Binary package | Source package |
| --- | --- | --- |
| msys-2.0.dll | msys2-runtime 3.6.5-1 | msys2-runtime-3.6.5-1.src.tar.zst |
| msys-gcc_s-seh-1.dll, msys-stdc++-6.dll | gcc-libs 15.2.0-1 | gcc-15.2.0-1.src.tar.zst |
| msys-iconv-2.dll | libiconv 1.18-1 | libiconv-1.18-1.src.tar.zst |

The source packages retain upstream code, MSYS2 patches and PKGBUILD recipes.
The runtime package contains a bare Git repository; its recipe selects
`cygwin-3.6.5` and applies the included patches. The GCC and libiconv packages
contain their upstream release archives. cdrdao's own pinned source remains
by SHA-256 in the runtime cache and is copied beside its executable.

Runtime licence texts and notices are retained under `licenses/cdrdao/`
and copied to the same subfolder in build/publish output. About reads only the
output `licenses/` tree: common notices at its root, native notices under
`ffmpeg/` and `cdrdao/`, and the application licence under `application/`.
Native copies use `Component.Document.txt`, such as `GCC.Runtime-Exception.txt`;
About derives origin/component/document labels directly from these names.
Source manifests remain shipped as supporting documentation, outside the UI list.
Common duplicate native licence entries are omitted from that list.
The single `GCC.GPLv3.txt` copy also supplies the GPL v3 terms incorporated by
`MSYS2.LGPLv3.txt`; the complete texts and exceptions remain unchanged.
Original upstream FFmpeg notices are also preserved in its distribution layout.
Preparation verifies each cdrdao DLL against the reviewed hash,
so updating the ZIP also requires reviewing this inventory.
The full upstream newlib notice collection is retained conservatively; its
individual sections have not all been proven applicable to this Windows binary.
No legal text was removed as part of filename and presentation simplification.
The pinned Windows ZIP contains four executables and four MSYS DLLs, with no
standalone notices. Preparation packages only `cdrdao.exe` and the four DLLs.
The output notices include CDRDAO and its incorporated cdparanoia attribution
from the pinned CDRDAO source, plus GNU libiconv attribution from the matched
1.18-1 package source.

### TagLibSharp

The 2.3.0 NuGet package identifies LGPL-2.1-only and the upstream repository,
but does not record a source commit. The upstream `TaglibSharp-2.3.0.0` tag
resolves to `b5ae84f2e84087bf160bb0471420200dd2b5d809`; its full archive is pinned
as release source evidence. Its test-only raw-samples submodule is not included
in GitHub's archive. This is not a claim of a reproducible NuGet binary build.
The library stays a separate, untrimmed managed DLL; source distribution and
replacement/relinking rights must remain available in a public release.

## Remaining FFmpeg work

The active Windows x64, Linux x64 and Apple Silicon bundles use Conda-forge FFmpeg
9.0.2 LGPL. The [runtime locks](../../vendor/versions.md#ffmpeg) identify the exact
packages and shipped files. Preparation publishes the original package licence
texts and the accompanying runtime lock. The FFmpeg core source archive matches
the SHA-256 in all three embedded FFmpeg recipes.

The source ZIP includes the embedded FFmpeg recipes and patches. It includes
checksum-matched Windows sources and package recipes for Cairo, FriBidi,
GdkPixbuf, Graphite2, LAME, GLib, GNU libiconv, GNU gettext (libintl), Pango,
librsvg and mpg123. FreeType source is also included. The librsvg release
lockfile pins the Rust registry archives in the ZIP. Linux and Apple Silicon
dependency source assembly remains open. Review additional statically linked
inputs where applicable. Windows portable builds require the Visual
C++ x64 redistributable and Windows setup declares it as a prerequisite.
Run clean-install and media workflow checks on the target hosts.

The Conda-forge OpenH264 DLL ships with Cisco's BSD-2-Clause copyright notice,
licence terms and disclaimer. Toolkit displays the bundled text under
**About → Third-party notices → FFmpeg · OpenH264 · BSD-2-Clause**.
Cisco describes separate terms for patent-pool coverage of its own binary
downloads. Coverage for the Conda-forge build remains unverified.
[Cisco OpenH264 FAQ](https://www.openh264.org/faq.html)

## Historical FFmpeg investigations

The following evidence describes earlier binary builds and the Windows
Conda-forge evaluation. The source archive assembled for the current runtime
uses the Conda-forge source pin and runtime locks.

The Windows and Linux BtbN archives each contain an LGPLv3 text. Preparation
preserves it and adds GPLv3, whose terms LGPLv3 incorporates. The two macOS
ZIPs contain binaries alone. Preparation supplies the recorded vendor notice
and GPL text for each macOS target.

The pinned binaries identify libvorbis 1.3.7 in the Windows and Linux
`libavcodec` libraries and in the Apple Silicon `ffmpeg` and `ffprobe` pairs. The
upstream BSD copyright notice now ships under `licenses/ffmpeg/` on all three
targets. [libvorbis 1.3.7 COPYING](https://github.com/xiph/vorbis/blob/v1.3.7/COPYING)

The retained October 31 BtbN release supplies both Windows and Linux
`lgpl-shared` builds. Both archive hashes match BtbN's checksums. The source
manifest pins FFmpeg commit `8dca1586f468bfcbb78ae3ed618af1361d9f140a` and
BtbN recipe commit `5031c6f1082414e53688d39e521849905071070d`, including
the recipe repository's patches.

The source manifest pins exact recipe revisions for components including
libvorbis, libogg, libass, TwoLAME, GNU libiconv, gnulib, zlib and libopus.
For libbluray it includes the top-level commit, its libudfread submodule at
`139a2194525f2745b98a98e4d8fa627d07440176`, and the separately configured
BtbN libudfread dependency at `b0bc6957e7e07d5f35391f210596d9eb71cffdd9`.
For Windows, `avformat-62.dll` contains native libbluray source-path strings
and identifies libbluray 1.5.1. The pinned recipe builds libbluray as a static
library from `f33a738f` and installs libudfread `b0bc6957` before configuring
libbluray. At that revision, libbluray's Meson build uses the external
libudfread when available and falls back to its own submodule otherwise.
This identifies the expected libudfread input from the recipe. No BD-J JAR
was found in the extracted Windows archive.

The Windows recipe also selects NVIDIA codec header revision `ced4f8eb`
for FFmpeg 8.1, and pins LAME at SVN revision 7113. The source manifest now
includes the AMF headers installed by the recipe (58 files including its
licence, checked against Git blob IDs), MinGW-w64, OpenCORE AMR and SoXR.
It also includes Shaderc's DEPS pins and selected nested sources for JPEG XL
(Highway), GLib, libplacebo, Mbed TLS and its crypto library, nghttp3, and zimg.
Twelve Rust crate versions were identified in source paths in Windows
`avcodec-62.dll`. Each downloaded crate matches its checksum in rav1e's
pinned Cargo.lock. The recipe runs `cargo update cc`, so the lockfile and
the visible DLL strings do not establish the full dependency resolution.
Rav1e declares `cc` under `[build-dependencies]`. Its changed version is
evidence that the recipe can resolve inputs during a build. It is not evidence
of an extra runtime crate linked into the DLL.
The Windows notice bundle now includes libudfread, OpenCORE AMR and SoXR
licence texts from their pinned sources.

Remaining work is to reconcile all linked inputs, nested build dependencies
and notices against the shipped Windows binaries. A recipe pin establishes
the source for that stage, while binary or build evidence is needed to
determine which stages contribute code. Locally generated archives without
an `origin` URL need their exact source reference and revision or existing
cache copy when rebuilding the source bundle.

The GitHub release asset digest matches the repository's pinned Windows ZIP.
The ZIP's PE imports name the FFmpeg shared DLLs and Windows libraries.
They do not enumerate external code linked statically into those DLLs.
The exact October 31 build logs and dependency image digest were not recovered.
The pinned workflow publishes dependency images under mutable tags and
disables build provenance attestations for those images. A neighboring run
or today's image would not establish the inputs to our archived binary.

### Windows replacement build scope

For a build we control, start from the current FFmpeg source revision with
shared libraries, autodetection disabled and GPL/nonfree modes disabled.
Keep FFmpeg's built-in local media support on the first pass to avoid narrowing
input compatibility accidentally. Pin only the external libraries Toolkit
requests by name, plus their actual build dependencies:

| External library | Toolkit path to preserve |
| --- | --- |
| LAME | MP3 audio export, lossless Audio+G to MP3+G, and AVI audio |
| libogg and libvorbis | Ogg/Vorbis audio export |
| OpenH264 | H.264 video for MP4 and MKV |

Keep `ffprobe` and test both executables against the built DLLs. Exercise all
six audio outputs, CD+G to MP4/MKV/AVI, lossless input variants, and Matroska
metadata remuxing. Preserve the build environment identity, source archives,
any patches, configure options, notices, and output hashes together. This
host has no installed WSL distribution and no Docker executable was found.
Use a prepared Windows native toolchain or a Linux cross-build runner to
build and verify a candidate before changing the pinned Windows runtime.

### Conda-forge Windows candidate

The tested candidate was `win-64/ffmpeg-9.0.2-lgpl_h9d1ee69_608.conda`,
SHA-256 `76962013d16bc46a51a258ef59347d054639e07c184f12db2fe44981daa5243f`.
The package carries its rendered build recipe, source pin and patches.
The binary reports `--disable-gpl`, `--enable-version3` and shared FFmpeg
libraries. The runtime dependency solve was recorded separately because
it can select packages newer than the original build inputs.

An isolated Windows probe resolved 77 packages and copied the normal PE
import closure for `ffmpeg.exe` and `ffprobe.exe` into the Toolkit layout.
The resulting folder contained 79 files (about 119 MB before compression)
from 42 packages. Both executables ran with a Windows-only PATH. This
probes local media workflows, with no claim about every optional loader,
format or dependency on a clean Windows machine. Before selecting it for
distribution, pin the runtime closure, review the redistributability of
its packages, and assemble the source and notices for the files shipped.

The Windows media suite passed 221 of 222 cases. The remaining test calls
`libvpx-vp9` to create a WebM fixture, an encoder absent from the candidate.
A separate test generated VP9/Opus WebM with BtbN, then used Toolkit's CLI
with the Conda runtime to tag it. The tags persisted and both encoded
stream hashes were unchanged. The automated tagging test now uses a
synthetic VP9/Opus fixture and checks both encoded stream hashes.

The opt-in Windows build now uses an exact package and per-binary checksum
lock. It extracts 61 files from 40 binary packages plus an intermediate
FreeType package for matching notices. The Windows UCRT and Microsoft Visual
C++ runtime must be available on the host. Their DLLs are excluded from the
candidate. This path collects upstream package notices, but the matching
source archive and clean Windows installer verification remain pending.

The Linux x64 `lgpl_hff9caec_808` and Apple Silicon `lgpl_ha3d7a35_8`
9.0.2 candidates were downloaded and their dependency plans resolved.
Both packages mark the executables with a Conda build-prefix placeholder.
A relocation and shared-library loading check on native runners is needed
before pinning their redistributable runtimes.

The [LAME SVN tree at revision 7113](https://svn.code.sf.net/p/lame/svn/!svn/rvr/7113/trunk/lame/)
was reachable and identified itself as that revision. The tested ZIP snapshot
URL returned 404. Export the exact tree with Subversion to assemble a local archive:

```text
svn export -r 7113 https://svn.code.sf.net/p/lame/svn/trunk/lame@7113 lame-r7113
```

The Windows and Linux binaries carry distinct configure strings. For example,
Linux enables OpenSSL, libxcb, PulseAudio and libdrm, while Windows enables
SChannel and Windows hardware integrations. The recipe contains sources for
other variants as well. Its complete script list cannot establish which inputs
were used for either binary. Finish matching enabled and transitive components,
their sources and their notices per target.
The full evidence output includes the FFmpeg runtime manifest and the currently
maintained FFmpeg notices alongside the source archives.
The package preparation step copies the following upstream library licence
texts according to the per-target lists in `vendor/ffmpeg/manifest.json`.

| Library | Prepared targets | Upstream notice or licence |
| --- | --- | --- |
| LAME | All three | [Library GPL v2](https://github.com/lameproject/lame/blob/master/COPYING) and [attribution](https://github.com/lameproject/lame/blob/master/README) |
| libass | All three | [ISC](https://github.com/libass/libass/blob/0.17.4/COPYING) |
| GNU libiconv | Windows and Linux | LGPL v2.1 text from [GNU libiconv](https://ftp.gnu.org/gnu/libiconv/) |
| libbluray | All three | LGPL v2.1 text from [VideoLAN's libbluray 1.5.0 source archive](https://download.videolan.org/pub/videolan/libbluray/1.5.0/) |
| TwoLAME | Windows and Linux | [LGPL v2.1](https://github.com/njh/twolame/blob/0.4.0/COPYING) |

The FFmpeg configure strings identify LAME, libass, libbluray and TwoLAME
on the targets shown. The displaced OSXExperts pair reported GNU libiconv 1.17.
Both BtbN builds
enable iconv and ship the GNU libiconv licence conservatively. Linux's
dynamic symbols also show calls to glibc's iconv, so its exact GNU libiconv
linkage needs confirmation.

The previous September BtbN release returned HTTP 404 during review.
Exact external-library revisions and any inputs fetched by the October recipe
still need verification. The upstream texts above cover the named libraries' base licences.
Their release links identify the source of the licence text. Exact build
revisions, transitive notices and complete corresponding source still need
review against the pinned Windows, Linux and macOS binaries.

Finish by recovering the exact build inputs and notices, or replacing the
native distribution with a build whose complete inputs can be retained and
verified. Replacing binaries needs conversion/media regression checks; this
licensing work has not changed them. macOS FFmpeg provenance remains separately
unresolved as recorded in the [vendor inventory](../../vendor/versions.md).

A follow-up found an archived index for the September 29 BtbN release, but no
verified build-recipe revision or complete corresponding source for that pair.
The displaced archives are in `artifacts/checks/ffmpeg-review/september-29/`.
Apple Silicon now uses Martin Riedl's FFmpeg and FFprobe 9.0.2 release. Both ZIPs
match the published checksums and contain ARM64 Mach-O executables. The source
bundle includes the upstream FFmpeg release source and the vendor's version
and configure report. The report identifies LAME, Vorbis and x264. The bundle
also includes recipe candidate `6a611e19870e197bc37c6e4c7fccddebd3715466`.
Its version files agree with the reported dependency versions where present.
The bundle now includes the candidate's release archives for LAME 4.0,
libass 0.17.5 and libvorbis 1.3.7. These versions match the vendor's report.
Its x264 script fetches the moving `master` archive, and the report gives only
`0.165.x`. The exact x264 revision and confirmation that the vendor used this
recipe are pending. Complete dependency sources and notices, native
execution and signing checks remain open. The previous OSXExperts archives and
notices are in `artifacts/checks/ffmpeg-review/osx-arm64-osxexperts/`. Before
removing or replacing any files, put the originals in a local review folder.
Any old Git LFS archives that cannot be cleared must also be excluded from the
history made public.

For replacement builds, retain the exact inputs and recipes at build time.
Check MP3 and AVI encoding with `libmp3lame`, Ogg encoding with `libvorbis`,
the existing karaoke video conversion tests, and `ffprobe` on each target.
Keep the existing archive layout and update the pinned hashes after native
verification. Build and execute the Apple Silicon variant on its target
architecture before publishing it.

## Upstream guidance

- [FFmpeg licensing and source checklist](https://ffmpeg.org/legal.html)
- [MSYS2 package licensing](https://www.msys2.org/license/)
- [MSYS2 source-package archive](https://repo.msys2.org/msys/sources/)
- [TagLibSharp release source](https://github.com/mono/taglib-sharp/tree/b5ae84f2e84087bf160bb0471420200dd2b5d809)

See the [licence inventory](../../licenses/README.md) for the other shipped notices.
