# Third-party licenses and notices

This directory keeps readable license texts for dependencies currently used or
stored in KaraokeToolkit. The repository does not yet have its own application
license. `vendor/VERSIONS.md` and the vendor manifests pin binary versions and
checksums; the files here do not by themselves complete a public release's
source or notice obligations.

| Component | Version / scope | License text | App package |
|---|---|---|---|
| Avalonia UI packages | 12.1.3 | `Avalonia.txt` (MIT) | Common |
| Inter font bundled by Avalonia.Fonts.Inter | 12.1.3 package | `Inter.OFL.txt` (SIL OFL 1.1) | Common |
| CommunityToolkit.Mvvm | 8.4.2 | `CommunityToolkit.Mvvm.txt` (MIT), `CommunityToolkit.Mvvm.ThirdPartyNotices.txt` | Common |
| FFMpegCore | 5.5.0 | `FFMpegCore.txt` (MIT) | Common |
| SharpCompress | 0.50.4 | `SharpCompress.txt` (MIT) | Common |
| TagLibSharp | 2.3.0 | `TagLibSharp.txt` (LGPL-2.1-only) | Common, separate managed DLL |
| Velopack | 1.2.161 | `Velopack.txt` (MIT) | Common, App installation/update lifecycle; checks disabled |
| SkiaSharp and native assets | 3.119.4 | `SkiaSharp.txt` (MIT) | Common |
| HarfBuzzSharp and native assets | 8.3.1.3 | `HarfBuzzSharp.txt` (MIT) | Common |
| ANGLE Windows native assets | 2.1.27548.20260419 | `ANGLE.txt` (BSD-style) | Windows |
| Instances (FFMpegCore dependency) | 3.1.0 | `Instances.txt` (MIT) | Common |
| MicroCom.Runtime (Avalonia dependency) | 0.11.6 | `MicroCom.Runtime.txt` (MIT) | Common |
| Microsoft.Extensions.Logging / DependencyInjection.Abstractions | 8.0.0 | `Microsoft.Extensions.txt` (MIT), `Microsoft.Extensions.ThirdPartyNotices.txt` | Common |
| Microsoft.IO.RecyclableMemoryStream (SharpCompress dependency) | 3.0.1 | `Microsoft.IO.RecyclableMemoryStream.txt` (MIT) | Common |
| Tmds.DBus.Protocol (Avalonia Linux integration) | 0.94.1 | `Tmds.DBus.Protocol.txt` (MIT) | Included with app notices |
| FFmpeg, Windows/Linux BtbN builds | 8.1 branch | `FFmpeg.LGPLv3.txt` and `FFmpeg.GPLv3.txt` | In `ffmpeg/` for those RIDs |
| FFmpeg, macOS Intel Evermeet build | 9.0.2 | `FFmpeg.GPLv3.txt` | In `ffmpeg/` for `osx-x64` |
| FFmpeg, macOS Apple Silicon OSXExperts build | 9.0 site label | `FFmpeg.GPLv2.txt` | In `ffmpeg/` for `osx-arm64` |
| CDRDAO | 1.2.6 | `CDRDAO.GPLv2.txt` | Bundled Windows x64 reader, with source archive |

The Avalonia text comes from its pinned source revision. The Community Toolkit,
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

Microsoft Extensions' licence and third-party notices were copied directly from
the restored 8.0.0 package; both abstraction packages contain identical texts.
The app includes these files in build and publish output. **About → Third-party
notices** displays the full texts shipped in `licenses/`, together with licence
files in the native tools' directories and FFmpeg's version/source manifest.

Except for TagLibSharp, NuGet package metadata lists the remaining runtime packages in the current
`KaraokeToolkit.App/obj/project.assets.json` as MIT except ANGLE and the
developer-only AvaloniaUI.DiagnosticsSupport package, which has no license
expression in its package metadata. Test-only packages are not in app releases.

Before public distribution, resolve the licenses and source obligations of the
MSYS DLLs in CDRDAO's Windows ZIP, and the matching source/notice obligations
for each FFmpeg binary. OSXExperts calls its downloads "for educational
purposes only"; that build's redistribution provenance remains unresolved.
The macOS binaries also need native execution and signing checks. This index is
an inventory, not a declaration that these releases are ready to distribute.
