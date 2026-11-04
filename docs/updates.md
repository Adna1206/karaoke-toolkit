# Updates and Velopack

App references Velopack 1.2.161 through NuGet. Core remains independent of the
installer/updater. `Program.Main` calls the Velopack lifecycle handler before
creating Avalonia UI, so install/update hook invocations can exit without opening
the application. Automatic application of downloaded updates on startup is disabled.

`KaraokeToolkit.App/Services/UpdateService.cs` contains the update-check scaffold:

- `ChecksEnabled` is `false`.
- `FeedUrl` is `null` until release hosting is configured.
- Disabled checks return without constructing an updater or accessing a feed.
- The method only checks; it never downloads, applies, or restarts anything.

No startup checks, update menu, scheduler or release feed are connected yet. Enabling
the flag alone does not schedule checks. Once hosting is ready, configure the
appropriate source (GitHub Releases needs Velopack's GitHub source adapter rather
than a regular web feed), wire the UI to asynchronous checks, handle network errors,
and ask before downloading/restarting. Applying updates must wait for file
operations to finish. Keep settings, recipes and recovery data outside installed
application files.

`scripts/Publish.ps1` restores the matching repository-local `vpk` tool and creates
Windows x64 and Linux x64 packages. See [publishing](publishing.md) for commands and
artifact locations. Release uploads, signing and installation/update testing remain
future work. The MIT notice ships in `licenses/Velopack.txt` and is listed in About.
