# Planned features

- [ ] **Command-line interface** — add a `KaraokeToolkit.Cli` console project for
  operations and saved recipes without the GUI or an AI client. Support explicit
  per-run settings, progress, cancellation, meaningful exit codes and optional JSON
  output. Define noninteractive overwrite/deletion policies so unattended runs never
  wait for GUI prompts. Share C# execution services and request/result contracts with
  MCP; MCP calls these directly rather than launching CLI commands. CLI can precede
  MCP, but is not a prerequisite.

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

- [x] **Recipe tagging steps** — saved filename pattern, tag options and ZIP settings;
  noninteractive execution with a frozen preview and parent-recipe crash recovery.

- [ ] **Recipe script steps** — add optional PowerShell/CMD steps with defined
  input/output paths, platform requirements, captured output, cancellation and
  explicit trust for imported scripts. Run without requesting elevation; lack
  of admin rights does not restrict access to the user's own files.

- [x] **Whole-recipe crash continuation** — checkpoint the saved recipe/settings,
  completed steps and output scope; resume child conversions and frozen move plans
  before continuing later steps. Startup recovery also includes tagging batches.
  Completed changes are retained; this is not whole-workflow rollback.

- [ ] **Modern karaoke graphics re-rendering** — investigate extracting or recognising
  CD+G lyrics and highlight timing to draw modern fonts, backgrounds and animations.
  This requires a separate renderer and accuracy checks; the current optional graphics
  smoothing only interpolates the original pixels and does not reconstruct lyrics.

- [ ] **Preview before processing** — show selected source groups, planned outputs,
  existing-file conflicts and originals scheduled for deletion before starting work.
  Explicitly deferred while input selection, completion reports, Explorer integration
  and validation are implemented. The current results window is an after-run report,
  not a preview or dry run.
  Tag Karaoke Files has its own editable metadata preview; this item concerns the
  broader conversion and file-management workflow.
