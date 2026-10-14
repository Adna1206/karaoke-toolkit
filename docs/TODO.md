# Planned features

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
