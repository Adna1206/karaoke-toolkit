# Planned features

- [ ] **macOS packaging via GitHub Actions** — consider macOS runners to publish
  Intel and Apple Silicon builds and run Velopack's macOS packaging tools, with
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
  credentials, and review runner/LFS costs. Keep in-app update checks disabled until
  the feed and update UI are ready.

- [ ] **Local MCP integration** — investigate a separate `KaraokeToolkit.Mcp`
  console project referencing Core and using the official C# MCP SDK over stdio.
  Start with operation/settings discovery, validation, tagging previews and reports;
  expand to conversions and recipes with explicit file-change permissions, progress,
  cancellation and recovery. The MCP client launches the server on demand; no GUI,
  hosted service or listening port is required. Prefer shipping the MCP executable
  alongside App in the same installer, inactive until launched by a client. GUI
  control is outside the initial scope.

- [ ] **MCP setup page** — add an Integrations → MCP page with setup instructions,
  the installed MCP executable path and a Copy Configuration button. Do not add a
  GUI on/off toggle: the client controls process startup and MCP works without App
  open. Consider future server-enforced access policies for read-only operations
  versus file changes, independently of whether the GUI is running.

- [ ] **Velopack credit in About** — add a concise visible credit such as
  "Installation and updates powered by Velopack" alongside the existing technology
  credits. Keep the full MIT notice in Third-party notices; describe updates as
  enabled only once the release feed and update UI are connected.

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
