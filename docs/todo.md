# Planned features

- [ ] **FFMpegCore migration** — upgrade the .NET wrapper from 5.5.0 to 6.0.0.
  Update FFprobe calls and FFmpeg process handling for the new APIs. Test audio
  conversion, MP4 encoding, cancellation and cleanup of partial output.

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
  existing preferences and processing behaviour. Current Settings work is stopped;
  resume this redesign only when requested.

- [ ] **Investigate the vendoring system** — evaluate how to acquire, pin, and
  retain native binaries, their exact source and build inputs, and notices.
  Compare upstream release archives with builds we control and account for Git
  LFS history before public distribution. See [distribution licensing](distribution-licensing.md).

- [ ] **Add a startup update check** — check Velopack Flow after recovery
  handling and notify the user when a newer version is available.
  See [updates](updates.md).

- [ ] **Add Velopack update downloads and installation** — verify the manual
  Flow check against a hosted release, then add download and apply/restart prompts. Verify
  that applying updates waits for file operations and preserves user data.
  See [updates](updates.md).

- [ ] **macOS packaging via GitHub Actions** — consider macOS runners to publish
  Apple Silicon builds and run Velopack's macOS packaging tools, with
  downloadable workflow artifacts. Review native dependency availability and
  redistribution requirements, test on macOS, and plan signing/notarization before
  public distribution. Local Windows development should not require a physical Mac.

- [ ] **GitHub CI and release automation** — consider pull-request/push build and
  test checks across supported platforms, retained test reports and package artifacts,
  and manually triggered or version-tagged releases. CLI platform testing now has
  `.github/workflows/cli-platforms.yml`; release/package automation remains planned. Reuse the publish scripts and
  pinned tools; validate shared versions, generate release notes/checksums, and
  optionally sign and upload installers/update feeds after checks pass. Decide on
  release hosting (including a separate releases-only repo), protect signing/upload
  credentials, and review runner/LFS costs.

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

- [ ] **Preview before processing** — show selected source groups, planned outputs,
  existing-file conflicts and originals scheduled for deletion before starting work.
  The current results window is an after-run report, not a preview or dry run.
  Tag Karaoke Files has its own editable metadata preview; this item concerns the
  broader conversion and file-management workflow.
