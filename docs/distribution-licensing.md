# Distribution licensing

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
# Explicitly permit downloading missing pinned source archives:
.\scripts\Prepare-DistributionSources.ps1 -Download
```

Output is a fresh directory under ignored `artifacts/distribution-sources/`,
containing the archives, checksums, manifest and this document. It is deliberately
labelled incomplete. Ordinary builds do not download these sources or add their
size to installed packages. No public hosting or source offer is configured.
When the inventory is complete, arrange source access alongside the binaries
under the applicable licence terms; generating local evidence is not publication.

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
under `vendor/cdrdao/source/` and is already copied beside its executable.

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
preserves it and adds GPLv3, whose terms LGPLv3 incorporates. The four macOS
ZIPs contain binaries alone. Preparation supplies the recorded vendor notice
and GPL text for each macOS target.

The pinned binaries identify libvorbis 1.3.7 in the Windows and Linux
`libavcodec` libraries and in both macOS `ffmpeg` and `ffprobe` pairs. The
upstream BSD copyright notice now ships under `licenses/ffmpeg/` on all four
targets. [libvorbis 1.3.7 COPYING](https://github.com/xiph/vorbis/blob/v1.3.7/COPYING)

The pinned Windows binary reports commit
`ff48edd8b29406ec2614db1137e972e77b15a10b`, and its source archive is now pinned
as evidence. The Windows and Linux manifests identify that same FFmpeg commit.
The package preparation step copies the following upstream library licence
texts according to the per-target lists in `vendor/ffmpeg/manifest.json`.

| Library | Prepared targets | Upstream notice or licence |
| --- | --- | --- |
| LAME | All four | [Library GPL v2](https://github.com/lameproject/lame/blob/master/COPYING) and [attribution](https://github.com/lameproject/lame/blob/master/README) |
| libass | All four | [ISC](https://github.com/libass/libass/blob/0.17.4/COPYING) |
| GNU libiconv | All four | LGPL v2.1 text from [GNU libiconv](https://ftp.gnu.org/gnu/libiconv/) |
| libbluray | Windows, Linux, macOS Intel | LGPL v2.1 text from [VideoLAN's libbluray 1.5.0 source archive](https://download.videolan.org/pub/videolan/libbluray/1.5.0/) |
| TwoLAME | Windows, Linux, macOS Intel | [LGPL v2.1](https://github.com/njh/twolame/blob/0.4.0/COPYING) |

The FFmpeg configure strings identify LAME, libass, libbluray and TwoLAME
on the targets shown. The macOS binaries define GNU libiconv symbols and
report version 1.19 on Intel and 1.17 on Apple Silicon. Preparation uses the
corresponding GNU release's licence text for each. Both BtbN builds
enable iconv and ship the GNU libiconv licence conservatively. Linux's
dynamic symbols also show calls to glibc's iconv, so its exact GNU libiconv
linkage needs confirmation.

The recorded BtbN release and build-recipe tag returned HTTP 404 during review.
Most exact external-library revisions, build scripts and patches remain
unverified. The upstream texts above cover the named libraries' base licences.
Their release links identify the source of the licence text. Exact build
revisions, transitive notices and complete corresponding source still need
review against the pinned Windows, Linux and macOS binaries.

Finish by recovering the exact build inputs and notices, or replacing the
native distribution with a build whose complete inputs can be retained and
verified. Replacing binaries needs conversion/media regression checks; this
licensing work has not changed them. macOS FFmpeg provenance remains separately
unresolved as recorded in the [vendor inventory](../vendor/versions.md).

## Upstream guidance

- [FFmpeg licensing and source checklist](https://ffmpeg.org/legal.html)
- [MSYS2 package licensing](https://www.msys2.org/license/)
- [MSYS2 source-package archive](https://repo.msys2.org/msys/sources/)
- [TagLibSharp release source](https://github.com/mono/taglib-sharp/tree/b5ae84f2e84087bf160bb0471420200dd2b5d809)

See the [licence inventory](../licenses/README.md) for the other shipped notices.
