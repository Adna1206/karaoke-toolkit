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
and the FFmpeg preparation prerequisites, including Zstandard-capable tar. The
.NET 10 SDK is required. Windows Setup declares the Visual C++ v14 x64
redistributable as a prerequisite. Portable Windows builds need it installed.
First-time NuGet/tool/runtime restores require network access. Build preparation
fetches pinned native archives on a cache miss and verifies their checksums.

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

The script produces unsigned local packages. Installing, signing, uploading,
changing Explorer registrations and bumping the app version are separate steps.

## Sharing a Windows test build

1. Commit the exact build inputs, tag the commit and build the installer from that
   tag. On a clean Windows machine without the Visual C++ x64 runtime, verify
   that Setup downloads and installs the prerequisite, Toolkit launches, and
   a media conversion completes. Open **About → Third-party notices** and
   confirm the OpenH264 entry displays its licence text.
2. Review the Windows source inventory and generate the third-party source archive
   by running `scripts/Prepare-DistributionSources.ps1 -Archive` from the
   tagged checkout. This is a separate step from building Setup. See the
   [distribution licensing record](distribution-licensing.md#remaining-ffmpeg-work)
   for its scope and remaining verification.
3. Attach Setup and the matching third-party source archive to the GitHub Release.
   Link both the third-party archive and the exact Toolkit tag from the release
   notes. GitHub automatically generates the tagged Toolkit source ZIP and
   tarball. The third-party archive is a separate release upload.
   A private repository link works when every installer recipient can download
   the matching source. A separate Toolkit source ZIP is optional when that
   access is in place. [GPLv3 §6](https://www.gnu.org/licenses/gpl-3.0.html#section6)
   · [GitHub release docs](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)

For the app's GitHub update check, also attach `releases.win.json` and the
matching full `.nupkg` to the same published release. Attach the Linux channel's
`releases.linux.json` and full package when publishing Linux. Keep the generated
asset names. Published prereleases are visible to installed apps on the matching
channel. With one architecture per operating system, each platform uses its
default channel. If a release adds a second architecture for an OS, give each
architecture a distinct channel and its own feed and full update package.
The app checks without credentials, so the repository must be public
for in-app checks to work. [Velopack GitHub publishing](https://docs.velopack.io/distributing/self-hosting)

Keep Linux and macOS downloads out of a Windows-only test release until those
targets have their own native runtime and source checks.

## Build versus publish

Ordinary Debug/Release builds remain fast development builds. Installer creation
is an explicit publish operation, not an automatic post-build action. This avoids
creating installers during tests or every Visual Studio Release build. A future
CI job can call this exact script after tests pass, then sign/upload packages.

The app version is currently YEAR.MONTH.BUILD. Bump it in `Directory.Build.props`
for a new release; the script passes the three-part value to Velopack. Keep the
package identity `KaraokeToolkit` stable across future updates.

After verification, the script removes successful staging. Failed staging stays available for diagnosis and can expire through [artifact cleanup](artifacts.md).
