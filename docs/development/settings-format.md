# Settings format and migrations

Desktop, CLI and MCP share this versioned profile through Core. See
[operation contracts](operations.md#shared-processing-settings) for the
settings catalogue and adapter rules.

## Filename and XML layout

Name the new profile `settings.xml` and keep the per-user data directory
resolved by Core's `UserDataPaths`. On Windows the new path is
`%LOCALAPPDATA%\KaraokeToolkitData\settings.xml`. The name covers shared settings
and desktop preferences. `.xml` is the conventional extension for plain XML.
`options.xmls` already contains plain XML. Its filename suffix changes neither
the document syntax nor the parser that reads it.
[RFC 7303 §9.1](https://www.rfc-editor.org/rfc/rfc7303.html#section-9.1)

Use a shallow hierarchy based on who owns the data and when it changes.
`Processing` belongs to Core and applies to operations on every interface.
`Desktop` belongs to App and stores preferences for the desktop experience.
`AppliedSettingsMigrations` belongs to the settings store and tracks completed
updates. Each owner can update its section while preserving the rest of the
document. Keep all three in one file so a settings change and its migration
record can be saved together. Add a top-level section only when the data has a
distinct owner or purpose. Processing categories such as audio and recovery are
already expressed by the catalogue IDs.

```xml
<?xml version="1.0" encoding="utf-8"?>
<KaraokeToolkit schemaVersion="1">
  <Processing>
    <Setting id="audio.mp3-bitrate">192</Setting>
    <Setting id="recovery.verification">XxHash128</Setting>
  </Processing>
  <Desktop>
    <Theme>System</Theme>
    <FavouriteOperations>
      <Operation>GroupByTrackId</Operation>
    </FavouriteOperations>
  </Desktop>
  <AppliedSettingsMigrations>
    <Id>2026-11-remove-options-xmls</Id>
  </AppliedSettingsMigrations>
</KaraokeToolkit>
```

- **XML declaration.** Write XML 1.0 as UTF-8. The declaration identifies the XML
  syntax and encoding, including for settings that contain non-ASCII paths or text.
- **`<KaraokeToolkit schemaVersion="1">`.** The root identifies the application
  profile and its attribute identifies the profile layout. Start at `1` and
  advance independently of the XML version and the toolkit release version.
- **`<Processing>`.** Put all shared settings here so Desktop, CLI and MCP can
  read the same typed values through Core. Their access rules still come from
  the catalogue and the respective adapter.
- **`<Setting id="audio.mp3-bitrate">192</Setting>`.** The attribute identifies
  the setting. Element text stores its value and XML handles escaping. The
  catalogue supplies the type, unit, default and validator. Dotted IDs already
  group settings by feature. Keeping `audio.mp3-bitrate` whole means that a UI
  regrouping has no effect on the saved path, and one reader handles every scalar.
- **`<Desktop>` and `<Theme>`.** App owns these elements. Use named elements for
  fixed preferences so their structure is readable and can evolve within App.
- **`<FavouriteOperations>` and `<Operation>`.** A collection gets a named
  container and one child per item. Use the stable operation name as the text and
  retain the list's meaningful order. Apply this pattern to other structured values.
- **`<AppliedSettingsMigrations>` and `<Id>`.** Keep migration history beside
  the settings it governs so one write can commit both. Store one immutable ID
  per applied release rule. The ID answers whether a rule has already run, even
  if the user has since changed that setting.

Use two-space indentation and a consistent section order for readable diffs.
Write shared settings in ordinal ID order. Read named sections and setting IDs
independently of sibling order. A missing setting uses its defined default. An
explicitly saved value stays present even if it equals the current default.
Preserve empty strings where the catalogue allows them. Use invariant scalar
formatting and stable enum names where the catalogue defines a named value.

The integer schema version describes the file layout independently of the app
release version. Adding an optional setting with a defined default can keep the
same schema version when older readers can safely skip it and preserve it on save.
Changing the meaning, type or location of an existing value requires an explicit
compatibility decision. Reject duplicate IDs in the versioned profile and retain
unrecognized data when saving a supported schema.

## One migration runner in Core

Evolve the existing `SettingsMigrations` component into the shared runner. Keep
structural migrations and release migrations together in Core's `Settings`
area, with separate rules for each:

| Change | Definition | How Core tracks it |
| --- | --- | --- |
| Change the file layout or convert saved values to a new representation | A tested C# step from one schema version to the next | The profile's `schemaVersion` |
| Apply a one-time release decision, such as setting recovery verification to xxHash128 | A bundled `settings-migrations.json` entry keyed by stable setting IDs, validated by the typed catalogue | An immutable migration ID in `AppliedSettingsMigrations` |
| Retire the old `options.xmls` file | A bundled `deleteAppDataFile` entry resolved by Core to the known old settings paths | An immutable migration ID recorded after cleanup succeeds |

The JSON manifest supports typed `set` operations and `deleteAppDataFile`.
Core accepts only registered cleanup targets. Currently the allowed target is
`options.xmls`, resolved to exact former profile paths. Layout changes belong
in C# because they may need to resolve duplicate fields, preserve older data
and handle conversion failures explicitly. Keep shipped migration IDs and ordered
steps available so a profile can skip releases. Apply schema steps first, then
apply pending release entries in manifest order. Before retiring a setting that
a shipped entry references, define how the runner will resolve that entry for
profiles where it is still pending. New profiles start at the current schema
with current defaults and mark bundled release entries complete on the first save.
Recipe settings and recovery journals keep their own snapshots.

Run the same migration pipeline for every interface. Desktop can commit on startup.
CLI and MCP reads resolve pending changes in memory. An authorized settings write
can commit them together with the requested change. If we need a support tool,
add a CLI preview such as `settings migrate --dry-run` that reports schema steps,
pending release IDs and any planned backup path. It should call the same Core runner.

## Preview cutover from defaults

Ship the layout and filename together in a preview release and call out the
settings reset in the release notes. Start the preview with current defaults.
The new profile has no dependency on the contents of `options.xmls`. Update
Desktop, CLI and MCP together so all three use the same active profile.

1. Use `settings.xml` if present. An invalid profile or unsupported schema
   produces an explicit error before any save or cleanup. If the file is absent,
   resolve the current defaults. Read-only access ends without writing to disk.
2. On Desktop startup or an authorized settings write, validate and atomically
   save the new profile under the existing cross-process lock. Keep pending
   value migrations and their records in the same transaction.
3. Run the pending cleanup step under the same lock. Target only the exact
   `options.xmls` paths previously resolved by `UserDataPaths`. An already
   absent file counts as success. Record the cleanup ID after successful
   deletion. File access failures leave the step pending and log a trace
   warning. A later startup or authorized write can retry safely without
   resetting the new profile.

For future schema changes within `settings.xml`, keep a pre-upgrade copy separate
from the rotating `.bak` before committing the replacement. Preserving old
preferences for a possible downgrade requires copying `options.xmls` before
starting this preview. State that in the release notes.

Before shipping the cutover, verify defaults with and without an old profile,
including malformed old XML. Check repeated runs, read-only access, failed
saves, interrupted cleanup and concurrent writes. Exercise an installed
upgrade and check that the old file disappears after the new profile is valid.
