# Planned features

- [ ] **Open multiple files from Windows Explorer** — make **Open in Karaoke
  Toolkit** accept the full Explorer selection and load it in one window.
  Use a shell handler that receives the selection as a batch. Preserve per-user
  registration, existing single-item and folder-background commands, and default
  file associations. Verify with multiple ZIPs, including paths with spaces.
  See [Windows Explorer integration](user/file-workflow-features.md#windows-explorer).

- [ ] **FFMpegCore migration** — upgrade the .NET wrapper from 5.5.0 to 6.0.0.
  Adapt the remaining wrapper calls and process handling to the new APIs.
  Keep the direct FFprobe JSON reader and verify audio conversion, MP4 encoding,
  cancellation and cleanup of partial output.

- [ ] **Karaoke key and tempo changes** — use [SoundTouch.Net](https://github.com/owoudenberg/soundtouch.net)
  to shift pitch in semitones and adjust tempo independently. Process decoded
  PCM through SoundTouch.Net, then encode the result with the existing audio
  pipeline. Keep karaoke graphics in sync when tempo changes and review
  LGPL-2.1-or-later distribution requirements.

- [ ] **Improve Clean Up Filenames** — let users choose which cleanup rules
  to apply and how characters are replaced. For example, allow underscores
  (`_`) to become spaces or apostrophes (`'`).

- [ ] **Tag to Name** — rename karaoke files using their existing metadata
  tags, such as artist and title.

- [ ] **Revamp the entire Settings menu** — revisit navigation/categories, grouping,
  labels, spacing and help across every tab as one coherent design. Reduce persistent
  explanatory text, use progressive disclosure for advanced features, and keep
  controls beside the content they affect. Include CLI integration: place shell
  selection within the relevant command/example/setup section rather than as a
  disconnected shared control. Review keyboard navigation, accessibility, small-window
  layouts and consistency of Apply/OK/Cancel versus immediate actions. Preserve
  existing preferences and processing behaviour. Resume the broader redesign
  when requested.

- [ ] **Linux and Apple Silicon dependency sources** — finish collecting the
  required sources, recipes and patches for the pinned FFmpeg runtime packages
  before distributing binaries for those platforms. See
  [distribution licensing](distribution/distribution-licensing.md#remaining-ffmpeg-work).

- [ ] **Smaller Windows FFmpeg build (optional)** — measure potential savings
  from an LGPL build configured for Toolkit's required codecs, filters and
  formats. Verify conversion workflows before replacing the current bundle.

- [ ] **Add Velopack update downloads and installation** — verify that an
  installed build detects a newer release on GitHub, then add download and
  apply/restart prompts. Verify that applying updates waits for file operations
  and preserves user data.
  See [updates](distribution/updates.md).

- [ ] **macOS packaging via GitHub Actions** — consider macOS runners to publish
  Apple Silicon builds and run Velopack's macOS packaging tools, with
  downloadable workflow artifacts. Review native dependency availability and
  redistribution requirements, test on macOS, and plan signing/notarization before
  public distribution. Local Windows development should not require a physical Mac.

- [ ] **GitHub CI and release automation** — extend the existing
  [CLI platform workflow](../.github/workflows/cli-platforms.yml) with broader
  checks as needed. Automate the [GitHub Releases workflow](distribution/publishing.md)
  using the pinned tools and existing scripts. Verify tagged versions and retain
  test reports and build artifacts. Include the installers, update feeds,
  third-party source archives and checksums. Review signing and upload
  credential handling.

- [ ] **Windows MSI installer** — extend `scripts/Publish.ps1` to generate an MSI
  through Velopack's WiX integration (`vpk pack --msi`) alongside the existing
  one-click Setup EXE and portable ZIP. Configure installation scope and setup
  pages, and test installation, upgrades and removal while preserving settings,
  recipes and recovery data. See [Velopack installers](https://docs.velopack.io/packaging/installer).

- [ ] **Recipe script steps** — add optional PowerShell/CMD steps with defined
  input/output paths, platform requirements, captured output, cancellation and
  explicit trust for imported scripts. Run without requesting elevation; lack
  of admin rights does not restrict access to the user's own files.

- [ ] **Modern karaoke graphics re-rendering** — investigate extracting or recognising
  CD+G lyrics and highlight timing to draw modern fonts, backgrounds and animations.
  This requires a separate renderer and accuracy checks; the current optional graphics
  smoothing only interpolates the original pixels and does not reconstruct lyrics.

- [ ] **Extend desktop processing previews** — show selected source groups,
  planned outputs, existing-file conflicts and originals scheduled for deletion
  for more conversion and file-management workflows. Build on the existing
  [CLI dry runs](user/cli.md#recipes-previews-and-quarantine) and the desktop
  previews for tagging and track grouping.
