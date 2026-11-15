# Publishing local packages

From the repository root, run:

```powershell
.\scripts\Publish.ps1
```

The default targets are Windows x64 and Linux x64. To package just one:

```powershell
.\scripts\Publish.ps1 -RuntimeIdentifier win-x64
.\scripts\Publish.ps1 -RuntimeIdentifier linux-x64
```

Use Windows PowerShell 5.1 or PowerShell 7 on Windows. Linux hosts need PowerShell 7
and the existing FFmpeg preparation prerequisites. The .NET 10 SDK is required.
First-time NuGet/tool/runtime restores require network access. Native media tools
come from the pinned vendor archives, not new downloads.

## What the script does

1. Reads the evaluated app Version and Product from MSBuild, including
   `Directory.Build.props`.
2. Restores repository-local `vpk` using `.config/dotnet-tools.json`, checking that
   its version matches the App's Velopack NuGet reference. No global tool is needed.
3. Runs `dotnet publish` for App, CLI and MCP in Release for each runtime, with .NET included.
   Trimming and single-file publishing are disabled to preserve dependency behaviour
   and the separate TagLibSharp library.
4. Checks required files, including FFmpeg/ffprobe, their manifest, common notices
   and Windows cdrdao. Existing build targets select the right vendor archives.
5. Runs Velopack packaging for each target and writes SHA256 checksums.

Each invocation uses fresh output directories so builds cannot pick up native files
from a different platform or overwrite earlier packages:

- Published files: `artifacts/publish/<version>/<run-id>/<rid>/`
- Packages: `artifacts/releases/<version>/<run-id>/<rid>/`

Windows produces a Setup EXE, portable ZIP, full update package and release metadata.
Setup creates Desktop and Start menu shortcuts automatically.
The Setup EXE has the evaluated app version appended before its extension, for
example `KaraokeToolkit-win-Setup-2026.11.7.exe`. Checksums use the final filename.
App, CLI and MCP outputs include `licenses/application/LICENSE` (GNU GPL v3)
and `licenses/application/NOTICE.txt` (GPL-3.0-or-later grant and copyright).
All displayed notices come from `licenses/`, with native texts named
`Component.Document.txt` in `licenses/ffmpeg/` and `licenses/cdrdao/`. About reads
only this tree and excludes audit/source manifests from its licence list.
Upstream FFmpeg notices are additionally preserved in its original layout.
CLI and MCP are included beside the desktop executable, sharing .NET, Core and
FFmpeg. MCP stays inactive until an MCP client launches it.
Linux produces a versioned AppImage (for example `KaraokeToolkit-2026.11.11.AppImage`),
a full update package and release metadata. Checksums use the final AppImage name. Linux disc
ripping still requires an installed cdrdao or a configured executable path; it is
not bundled in the Linux package. Velopack's Windows cross-packaging tool recognises
ELF executables, giving bundled Linux binaries executable permissions in the AppImage.
After downloading an AppImage on Linux, mark the outer file executable if necessary.

The script does not install packages, upload releases, sign
packages, change Explorer registrations or bump the app version. Packages are local
unsigned build outputs. Signing, real install/upgrade/uninstall testing, Linux runtime
testing and distribution/source obligations remain release-readiness work.
See [distribution licensing](distribution-licensing.md) for the separate source
assembly command and unresolved FFmpeg inputs. Its current output is incomplete
source evidence, not a complete public-release source bundle.

## Build versus publish

Ordinary Debug/Release builds remain fast development builds. Installer creation
is an explicit publish operation, not an automatic post-build action. This avoids
creating installers during tests or every Visual Studio Release build. A future
CI job can call this exact script after tests pass, then sign/upload packages.

The app version is currently YEAR.MONTH.BUILD. Bump it in `Directory.Build.props`
for a new release; the script passes the three-part value to Velopack. Keep the
package identity `KaraokeToolkit` stable across future updates.

After verification, the script removes successful staging. Failed staging stays available for diagnosis and can expire through [artifact cleanup](artifacts.md).
