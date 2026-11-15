# Distribution licensing

The [source availability register](source-availability.md) collects versions,
source links and outstanding matches for download-page preparation. This guide
retains the detailed evidence and investigation notes.

Karaoke Toolkit's original code and assets are licensed under **GPL-3.0-or-later**.
Copyright © 2026 André Almeida (ADNA). See [LICENSE](../LICENSE)
and [the application notice](../NOTICE.txt). Third-party components retain their
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
for FFmpeg 8.1, and pins LAME at SVN revision 7113. The remaining review
includes source inputs fetched by nested build steps. Examples are
Shaderc's pinned dependencies in its DEPS file, JPEG XL's Highway submodule,
and dependency resolution for the Rust builds. A recipe pin establishes
provenance for that stage, but does not establish that every stage was
linked into the shipped binary.

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
unresolved as recorded in the [vendor inventory](../vendor/versions.md).

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

See the [licence inventory](../licenses/README.md) for the other shipped notices.
