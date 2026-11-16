# Updates and Velopack

App references Velopack 1.2.161 through NuGet. Core remains independent of the
installer/updater. `Program.Main` calls the Velopack lifecycle handler before
creating Avalonia UI, so install/update hook invocations can exit without opening
the application. Automatic application of downloaded updates on startup is disabled.

Select **Help → Check for updates** to check
[Toolkit's GitHub Releases](https://github.com/Adna1206/karaoke-toolkit/releases).
Enable **Tools → Settings → General → Check for Updates on Startup** to check
after the main window opens and startup recovery handling finishes. The setting
defaults to off. The check runs asynchronously and only shows a dialog when a
newer version is available. The app uses the installed package ID and channel.
Windows packages currently use `KaraokeToolkit` and `win`. Published GitHub
prereleases can be offered to installations on the matching channel. Drafts
are hidden. The app checks anonymously, so the repository must be public for
the built-in check to work. Unpackaged development builds report that an
installation is needed. [Velopack update sources](https://docs.velopack.io/integrating/update-sources)

The manual check reports a newer version, no update, or a failed request. Downloads
and apply/restart controls are planned. To verify availability end to end,
install a build with the GitHub source and publish a newer GitHub Release with
matching channel assets. Installations built with the old Flow source need the
GitHub-enabled build installed first to see releases on GitHub. Applying updates
must wait for file operations to finish.

On Windows, settings and favourites, saved recipes, and the recovery index now
live in `%LOCALAPPDATA%\KaraokeToolkitData`. Startup copies surviving files from
the old `%LOCALAPPDATA%\KaraokeToolkit` directory into missing destinations.
The installed app and the CLI/MCP use the same recovery index location.

Before upgrading from a version that stores data in the install directory,
close the installed app and launch the new portable Windows build once. This
copies the data while the old files are still available. Then close the portable
build and run Setup. Setup can replace the old directory before the installed app
starts. Files removed by an earlier installer require a backup. Other platforms
continue using the existing local application data location.

`scripts/Publish.ps1` restores the matching repository-local `vpk` tool and creates
Windows x64 and Linux x64 packages. See [publishing](publishing.md) for commands and
artifact locations. Release uploads, signing and installation/update testing remain
future work. The MIT notice ships in `licenses/Velopack.txt` and is listed in About.
