# Group by Track ID

In **Folder Tools**, select files or folders and open **Group by Track
ID**. Choose whether to scan folders for files, with optional recursion, or
group each selected folder as a whole. Whole folders keep their contents and
names. Files keep their names and matching audio and graphics are grouped together.
The preview lists files without a recognizable track ID under skipped items.
The preview scans on open and when you select **Refresh**. Grouping options
update the tree from the scanned files and folders. If whole-folder mode uses a
different destination, **Refresh** checks it for conflicts before you can apply
the plan. The status bar shows counts and
percentages when a stage has a known total.
The preview opens in the main workspace. **Back** returns to the selected
operation and inputs, with confirmation if you changed the grouping settings.
After you select **Move** or **Copy**, the footer shows an animated bar while
the app prepares recovery for the approved items. Once item processing
begins, the item and total progress bars show completion percentages.

An ID can have letters directly after the digits. `ABC-369A` and `ABC-369B`
group with `ABC-369` by number and keep their full names.

- **Number ranges** uses a configurable range size and origin. With 50 starting
  at 1, `ABC33601` and `ABC-33650` go in `ABC-33601 to ABC-33650` when the
  range separator is set to **to**. `ABC-33600` goes in the preceding range.
- **Trailing digits** can name the same interval with `x`, `X`, `#`, or the
  range start followed by `s`. Optional automatic parents add one masked digit
  at each level. You can limit parent depth and skip single-child parents.
- Whole-folder mode understands names such as `ABC-31xxx` and
  `ABC-31000 to ABC-31999`. The full interval must fit inside the destination
  group. Range-start labels such as `ABC-31000s` are ambiguous as inputs.
  Distinct prefixes remain independent.

**Minimum ID digits** controls leading zeroes in generated group names. Leave it
at `0` to use the source width. Choose `1` to `9` to set a minimum width. With
five digits, the interval 300–399 can appear as `ABC-003xx` or
`ABC-00300 to ABC-00399`. Larger numbers keep all their digits. The setting
applies to automatic parents and to groups that contain whole folders.
It preserves source names and uses the numeric IDs to determine group membership
and parent depth. Added zeroes do not create extra parent levels.

The desktop fills in the destination when the selection resolves to one folder.
For files and whole-folder items, it uses their containing folder. For a folder
being scanned, it uses the selected folder itself. When the selection has no
single destination, choose a folder before the preview opens. Items already
at their planned path are skipped.

Review the live destination tree before applying. Use **Expand all** and
**Collapse all** beside **Refresh** to open or close every branch.
When there are skipped items or conflicts, drag the divider above their panel
to resize it. Drag it to the bottom to hide the panel and back up to reopen it.
Resolve conflicts shown in the preview before applying. Applying uses the
approved list. A missing source or newly occupied destination skips the affected
group, and new files enter the list when you refresh. The desktop preview
defaults to Move, which requires a same-volume
destination. CLI and MCP default to Copy.
Interrupted publication can be resumed from the saved plan using Recover.
The same operation is available through the [CLI](cli.md) and [MCP](mcp.md).
