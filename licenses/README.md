# Third-party licenses and notices

This directory keeps readable license texts for dependencies currently used or
stored in KaraokeToolkit. Karaoke Toolkit's original code and assets use
GPL-3.0-or-later; see [LICENSE](../LICENSE) and [NOTICE.txt](../NOTICE.txt).
`vendor/versions.md` and the vendor manifests pin binary versions and
checksums; the files here do not by themselves complete a public release's
source or notice obligations.

Copyright © 2026 André Almeida (ADNA). The application licence
does not replace third-party licences or resolve source-distribution obligations. See the
[distribution licensing record](../docs/distribution-licensing.md) for verified
runtime source matches, source assembly and remaining FFmpeg work.

| Component | Version / scope | License text | App package |
|---|---|---|---|
| Avalonia UI packages | 12.1.6 | `Avalonia.txt` (MIT) | Common |
| Inter font bundled by Avalonia.Fonts.Inter | 12.1.6 package | `Inter.OFL.txt` (SIL OFL 1.1) | Common |
| CommunityToolkit.Mvvm | 8.4.2 | `CommunityToolkit.Mvvm.txt` (MIT), `CommunityToolkit.Mvvm.ThirdPartyNotices.txt` | Common |
| FFMpegCore | 5.5.0 | `FFMpegCore.txt` (MIT) | Common |
| SharpCompress | 0.50.8 | `SharpCompress.txt` (MIT) | Common |
| TagLibSharp | 2.3.0 | `TagLibSharp.txt` (LGPL-2.1-only) | Common, separate managed DLL |
| Velopack | 1.2.161 | `Velopack.txt` (MIT) | Common, App installation/update lifecycle; checks disabled |
| ModelContextProtocol / ModelContextProtocol.Core | 2.4.0 | `ModelContextProtocol.txt` (Apache-2.0 plus retained MIT notices) | MCP executable, dormant until client-launched |
| Microsoft.Extensions.AI.Abstractions | 10.11.0 | `Microsoft.Extensions.txt` (MIT), `Microsoft.Extensions.ThirdPartyNotices.txt` | MCP SDK dependency |
| SkiaSharp and native assets | 3.119.4 | `SkiaSharp.txt` (MIT) | Common |
| HarfBuzzSharp and native assets | 8.3.1.3 | `HarfBuzzSharp.txt` (MIT) | Common |
| ANGLE Windows native assets | 2.1.27548.20260419 | `ANGLE.txt` (BSD-style) | Windows |
| Instances (FFMpegCore dependency) | 3.1.0 | `Instances.txt` (MIT) | Common |
| MicroCom.Runtime (Avalonia dependency) | 0.11.6 | `MicroCom.Runtime.txt` (MIT) | Common |
| Microsoft.Extensions components | 11.0.0 | `Microsoft.Extensions.txt` (MIT), `Microsoft.Extensions.ThirdPartyNotices.txt` | Shared logging/DI abstractions aligned at 11.0.0; MCP hosting components |
| Microsoft.IO.RecyclableMemoryStream (Avalonia diagnostics dependency) | 3.0.1 | `Microsoft.IO.RecyclableMemoryStream.txt` (MIT) | App output |
| Tmds.DBus.Protocol (Avalonia Linux integration) | 0.94.1 | `Tmds.DBus.Protocol.txt` (MIT) | Included with app notices |
| FFmpeg, Windows/Linux BtbN builds | 8.1 branch | `FFmpeg.LGPLv3.txt` and `FFmpeg.GPLv3.txt` | In `ffmpeg/` for those RIDs |
| FFmpeg, macOS Apple Silicon Martin Riedl build | 9.0.2 | `FFmpeg.GPLv3.txt` | In `ffmpeg/` for `osx-arm64` |
| libvorbis in the pinned FFmpeg builds | 1.3.7, all three RIDs | `ffmpeg/libvorbis.BSD-3-Clause.txt` | In `licenses/ffmpeg/` for all three RIDs |
| LAME, libass, GNU libiconv, libbluray, TwoLAME in the pinned FFmpeg builds | Per-target inventory in `vendor/ffmpeg/manifest.json` | Upstream licence texts and LAME attribution in `ffmpeg/` | In `licenses/ffmpeg/` for the targets listed in the manifest |
| CDRDAO | 1.2.6 | `CDRDAO.GPLv2.txt` | Bundled Windows x64 reader, with source archive |
| MSYS2 runtime | 3.6.5-1 | LGPL v3 or later with linking exception; additional newlib notices, under `licenses/cdrdao/` | Windows `licenses/cdrdao/` |
| GCC runtime DLLs | gcc-libs 15.2.0-1 | GPL v3 or later with GCC Runtime Library Exception 3.1, under `licenses/cdrdao/` | Windows `licenses/cdrdao/` |
| GNU libiconv | 1.18-1 | LGPL v2.1 or later and copyright attribution, under `licenses/cdrdao/` | Windows `licenses/cdrdao/` |

The versions above describe the current dependency references. The Avalonia text
was collected from the source revision pinned by the earlier 12.1.3 package.
The Community Toolkit,
SkiaSharp, HarfBuzzSharp and ANGLE files come from the exact restored NuGet
packages. Inter's OFL text comes from the font project's upstream repository.
The FFmpeg GPL texts came from pinned FFmpeg source revisions, the LGPL text
from the BtbN archive, and CDRDAO's GPL text from its pinned source archive.
The additional runtime notices come from the exact upstream revisions recorded
in their restored NuGet packages:

Velopack's MIT text comes from source revision
`92d6a1c91716729d449034df5c50307dcce39493`, recorded in the 1.2.161 NuGet package.
The .NET 10 target adds no transitive NuGet dependencies.

TagLibSharp is an unmodified NuGet reference, loaded as a separate DLL. Its package
declares LGPL-2.1-only; the full LGPL v2.1 text is included in `TagLibSharp.txt`.
Copyright © 2006–2007 Brian Nickel; © 2009–2020 other contributors.
[TagLibSharp source and releases](https://github.com/mono/taglib-sharp) and
[the pinned 2.3.0 package](https://www.nuget.org/packages/TagLibSharp/2.3.0)
identify the library used. Retain the licence, allow users to replace/relink the
library, and provide corresponding library source as required when distributing.

- [Instances licence](https://github.com/rosenbjerg/Instances/blob/9322a21ebe4d9d438190cd543850315edc1a5fe1/LICENSE)
- [MicroCom licence](https://github.com/kekekeks/MicroCom/blob/76785efcafd91b5902fd19dd11145f6dd655b7b4/LICENSE)
- [RecyclableMemoryStream licence](https://github.com/microsoft/Microsoft.IO.RecyclableMemoryStream/blob/e29a28387da9018fa9605a1dcb3f7a0435aa9974/LICENSE)
- [Tmds.DBus licence](https://github.com/tmds/Tmds.DBus/blob/b4a7fed0b878f74cb54f7cca84d2889af4e596ba/COPYING)

The checked-in Microsoft.Extensions licence and legacy third-party notices came
from the restored 8.0.0 package. `Microsoft.Extensions.11.ThirdPartyNotices.txt`
matches the notice file shipped by the restored 11.0.0 packages that contain it.
Verify the retained Avalonia, MCP SDK and Microsoft.Extensions.AI notices
against the upgraded packages before distribution.
The app includes these files in build and publish output. **About → Third-party
notices** reads only `licenses/`: common texts at its root and native notice texts
in `ffmpeg/` and `cdrdao/`. Native copies use `Component.Document.txt`; their folder
and filename supply the display label. Audit/source manifests remain available
as documentation and are not licence-list entries. Common duplicate native texts
are also omitted from that list. The application licence is copied to
`licenses/application/` for the separate Application licence tab. This Markdown inventory
remains available in the package as documentation.

Except for TagLibSharp, NuGet package metadata lists the remaining runtime packages in the current
`KaraokeToolkit.App/obj/project.assets.json` as MIT except ANGLE and the
developer-only AvaloniaUI.DiagnosticsSupport package, which has no license
expression in its package metadata. Test-only packages are not in app releases.

The four MSYS DLLs in CDRDAO's Windows ZIP now have byte-for-byte upstream
package matches, shipped licence notices and checksummed source-package
references. The source assembly command prepares those archives locally;
public release source access still needs arranging. TagLibSharp release source
and the pinned Windows/Linux FFmpeg core source are also recorded in
`vendor/distribution-sources.json`. FFmpeg's external-library sources, notices
and exact build recipe are not yet complete; see the distribution licensing record.
The Apple Silicon build now has pinned upstream release source and vendor
build metadata. The exact recipe revision and complete corresponding source
still need verification.
The macOS binaries also need native execution and signing checks. This index is
an inventory, not a declaration that these releases are ready to distribute.
