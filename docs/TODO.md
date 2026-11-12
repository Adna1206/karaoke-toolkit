# Planned features

- [ ] **Recipe script steps** — add optional PowerShell/CMD steps with defined
  input/output paths, platform requirements, captured output, cancellation and
  explicit trust for imported scripts. Run without requesting elevation; lack
  of admin rights does not restrict access to the user's own files.

- [ ] **Whole-recipe crash continuation** — persist completed steps and their
  output paths so recovery of an interrupted conversion can continue its recipe.
  The initial builder reuses per-conversion recovery but does not resume the
  remaining recipe automatically or roll back completed steps.

- [ ] **Modern karaoke graphics re-rendering** — investigate extracting or recognising
  CD+G lyrics and highlight timing to draw modern fonts, backgrounds and animations.
  This requires a separate renderer and accuracy checks; the current optional graphics
  smoothing only interpolates the original pixels and does not reconstruct lyrics.

- [ ] **Preview before processing** — show selected source groups, planned outputs,
  existing-file conflicts and originals scheduled for deletion before starting work.
  Explicitly deferred while input selection, completion reports, Explorer integration
  and validation are implemented. The current results window is an after-run report,
  not a preview or dry run.
